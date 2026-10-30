// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264MultiSliceTestInput
    {
        private const int PictureCount = 18;
        private const int FrameRate = 6;
        private const int PictureBytes = 96 * 80 * 3 / 2;

        public static async Task VerifyAsync(string root, string name, int slicesPerPicture)
        {
            string path = Path.Combine(root, name + ".mp4");
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".sequence.yuv"));
            MediaBufferAllocator allocator = new(2 * 1024 * 1024);
            await using (RandomAccessStreamByteSource source = new(File.OpenRead(path)))
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                H264DecoderOptions options = new();
                H264Configuration configuration = H264Configuration.Parse(reader.Streams[0].Configuration.Span, options);
                H264RandomAccessInspector inspector = new(reader.Streams[0]);
                int idrPictures = 0;
                int bPictures = 0;
                int transformed = 0;
                int pictures = 0;
                int gopStart = 0;
                using H264ReferenceBuffer references = new();
                H264PictureOrderState order = new();
                while (true)
                {
                    using MediaPacket? packet = await reader.ReadPacketAsync();
                    if (packet is null)
                    {
                        break;
                    }
                    H264AccessUnit unit = H264AccessUnitReader.Read(packet.Data, configuration, options);
                    IReadOnlyList<H264SliceRegion> slices = H264SliceHeaders.Read(unit, options, default);
                    H264SliceHeader header = slices[0].Header;
                    if (slices.Count != slicesPerPicture || inspector.IsIdr(packet) != header.IsIdr)
                    {
                        throw new InvalidDataException("The multislice reference has an unexpected slice count or IDR classification.");
                    }
                    if (header.IsIdr)
                    {
                        idrPictures++;
                        gopStart = pictures;
                    }
                    if (header.Kind == H264SliceKind.B)
                    {
                        bPictures++;
                    }
                    H264PictureOrderState next = order.Advance(header);
                    H264Picture picture = H264MultiSliceDecoder.Decode(unit, slices, references, next.Current, allocator, options, default);
                    bool stored = false;
                    try
                    {
                        int presentation = gopStart + next.Current.Value / 2;
                        H264TestInput.VerifyPicture(picture, expected.AsSpan(presentation * PictureBytes, PictureBytes), pictures);
                        foreach (byte transform in picture.Transforms8x8)
                        {
                            transformed += transform;
                        }
                        references.Store(picture, header, next.Current);
                        stored = true;
                    }
                    finally
                    {
                        if (!stored)
                        {
                            picture.Dispose();
                        }
                    }
                    order = next;
                    pictures++;
                }
                if (pictures != PictureCount || idrPictures != 2 || bPictures == 0 ||
                    (configuration.Pictures.Values.Any(picture => picture.Transform8x8) && transformed == 0))
                {
                    throw new InvalidDataException("The multislice reference does not exercise its expected picture syntax.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Multislice reconstruction retained buffer storage.");
            }

            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource publicSource = new(File.OpenRead(path));
            await using VideoSession session = await engine.OpenVideoAsync(publicSource, 0);
            for (int index = 0; index < PictureCount; index++)
            {
                using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("The multislice session ended early.");
                H264TestInput.VerifyFrame(frame, expected.AsSpan(index * PictureBytes, PictureBytes));
                if (frame.Timestamp is not MediaTimestamp timestamp || timestamp.CompareTo(new(index, new TimeBase(1, FrameRate))) != 0)
                {
                    throw new InvalidDataException("The multislice session changed presentation order or timestamps.");
                }
            }
            if (await session.ReadFrameAsync() is not null)
            {
                throw new InvalidDataException("The multislice session produced extra pictures.");
            }
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("The multislice session retained buffer storage.");
            }
        }
    }
}
