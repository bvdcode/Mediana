// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class H264BidirectionalPictureTestInput
    {
        public static async Task VerifySessionAsync(string root, string name)
        {
            H264BidirectionalFixture fixture = H264BidirectionalFixture.Get(name);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv"));
            MediaEngine engine = Engine();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            for (int index = 0; index < fixture.PictureCount; index++)
            {
                using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("The B video ended before its expected frame count.");
                H264TestInput.VerifyFrame(frame, expected.AsSpan(index * fixture.PictureBytes, fixture.PictureBytes));
                if (frame.Timestamp is not MediaTimestamp timestamp || timestamp.CompareTo(new MediaTimestamp(index, new TimeBase(1, fixture.FrameRate))) != 0
                    || frame.Duration is not MediaDuration duration || new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new MediaTimestamp(1, new TimeBase(1, fixture.FrameRate))) != 0)
                {
                    throw new InvalidDataException("The public B session changed presentation order or frame timing.");
                }
            }
            if (await session.ReadFrameAsync() is not null)
            {
                throw new InvalidDataException("The B video has unexpected extra output.");
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The public B session retained frame storage.");
            }
        }

        public static async Task VerifyPreviewAsync(string root, string name)
        {
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + ".mp4")));
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0);
            using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(new MediaTimestamp(150, new TimeBase(1, 1000)))
                ?? throw new InvalidDataException("The requested B preview is missing.");
            if (frame.Timestamp is not MediaTimestamp timestamp || timestamp.CompareTo(new MediaTimestamp(2, new TimeBase(1, 10))) != 0)
            {
                throw new InvalidDataException("The B preview selected the wrong presentation timestamp.");
            }
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".frame2.srgb.rgba"));
            MediaBufferAllocator allocator = new(128 * 1024);
            using (VideoFrame preview = SrgbPreview.Fit(frame, session.Stream.DisplayMatrix!.Value, 64, 64, allocator))
            using (MediaBuffer png = PngEncoder.Encode(preview, allocator))
            {
                if (preview.Format.Width != 64 || preview.Format.Height != 43 || preview.Timestamp != frame.Timestamp)
                {
                    throw new InvalidDataException("The B preview has unexpected dimensions or timing.");
                }
                PngTestInput.VerifyPreview(png.Memory.Span, 64, 43, reference, 96, (x, y) => (x * 96 / 64, y * 64 / 43), 1);
            }
            frame.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The B preview retained frame storage.");
            }
        }

        private static MediaEngine Engine() => new MediaEngineBuilder()
            .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie }))
            .AddVideoDecoder(new H264DecoderFactory()).Build();

        public static async Task VerifyPicturesAsync(string root, string name)
        {
            H264BidirectionalFixture fixture = H264BidirectionalFixture.Get(name);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv"));
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            int bPictures = 0;
            int temporalPictures = 0;
            int transformedBBlocks = 0;
            int gopStart = 0;
            using (H264ReferenceBuffer references = new())
            {
                H264PictureOrderState order = new();
                for (int index = 0; index < fixture.PictureCount; index++)
                {
                    (H264Configuration configuration, byte[] nal) = await H264TestInput.ReadSliceAsync(Path.Combine(root, name + ".mp4"), index);
                    H264BitReader bits = H264Nal.Payload(nal, H264Nal.Type(nal));
                    H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
                    order = order.Advance(header);
                    if (header.Picture.Cabac != fixture.Cabac || header.Sequence.Width != fixture.Width || header.Sequence.Height != fixture.Height
                        || (header.Kind != H264SliceKind.I && header.CabacInitIdc != fixture.Initialization))
                    {
                        throw new InvalidDataException("The B sequence has unexpected parameters.");
                    }
                    if (header.IsIdr)
                    {
                        gopStart = index;
                    }
                    if (header.Kind == H264SliceKind.B)
                    {
                        bPictures++;
                        if (!header.DirectSpatialMotionPrediction)
                        {
                            temporalPictures++;
                        }
                    }
                    H264Picture picture = header.Kind switch
                    {
                        H264SliceKind.I or H264SliceKind.P => H264PictureDecoder.Decode(ref bits, header, references.GetList0(header), allocator),
                        H264SliceKind.B => H264BidirectionalPictureDecoder.Decode(ref bits, header, references.GetBidirectionalLists(header, order.Current), order.Current, allocator),
                        _ => throw new InvalidDataException("The B reference sequence has an unexpected picture kind.")
                    };
                    bool stored = false;
                    try
                    {
                        if (header.Kind == H264SliceKind.B)
                        {
                            foreach (byte transform in picture.Transforms8x8)
                            {
                                transformedBBlocks += transform;
                            }
                        }
                        int presentation = gopStart + order.Current.Value / 2;
                        H264TestInput.VerifyPicture(picture, expected.AsSpan(presentation * fixture.PictureBytes, fixture.PictureBytes), index);
                        references.Store(picture, header, order.Current);
                        stored = true;
                    }
                    finally
                    {
                        if (!stored)
                        {
                            picture.Dispose();
                        }
                    }
                }
            }
            if (bPictures != fixture.BPictureCount || temporalPictures != fixture.TemporalPictureCount
                || expected.Length != fixture.PictureCount * fixture.PictureBytes || allocator.AllocatedBytes != 0
                || (fixture.RequireTransform8x8 && transformedBBlocks == 0))
            {
                throw new InvalidDataException("The B sequence has an unexpected picture count or retained storage.");
            }
        }
    }
}
