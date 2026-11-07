// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using Mediana.Codecs.H264;
using Mediana.Codecs.Mpeg4Visual;
using Mediana.Containers.Avi;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class Mpeg4ContainerTestInput
    {
        internal static async Task VerifyAsync(string root, string name, string extension, TimeBase cadence)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".yuv"));
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new AviReaderFactory())
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new Mpeg4DecoderFactory()).AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(32 * 1024 * 1024, MemoryPool<byte>.Shared);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, name + "." + extension)));
            List<MediaTimestamp> positions = [];
            int frameBytes = 0;
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                if (session.Stream.CodecId != "mpeg4" || !session.CanSeek)
                { throw new InvalidDataException("The container did not expose seekable MPEG-4 Visual."); }
                while (await session.ReadFrameAsync() is VideoFrame frame)
                {
                    using (frame)
                    {
                        frameBytes = frame.Planes.Sum(plane => plane.Height * plane.RowBytes);
                        Verify(frame, positions.Count);
                        positions.Add(frame.Timestamp!.Value);
                        if (positions.Count == 1)
                        {
                            using VideoFrame preview = SrgbPreview.FitWithColorPolicy(frame, SdrPreviewColorPolicy.Resolve,
                                VideoDisplayMatrix.Identity, 32, 32, allocator);
                            if (preview.Format.Width > 32 || preview.Format.Height > 32)
                            { throw new InvalidDataException("The MPEG-4 Visual preview exceeded its dimensions."); }
                        }
                    }
                }
                if (positions.Count < 2 || positions.Count * frameBytes != expected.Length)
                { throw new InvalidDataException("The MPEG-4 Visual sequence has the wrong picture count."); }
                for (int index = positions.Count - 1; index >= 0; index--)
                {
                    await session.SeekAsync(positions[index]);
                    using VideoFrame frame = await session.ReadFrameAtOrAfterAsync(positions[index])
                        ?? throw new InvalidDataException("MPEG-4 Visual seeking lost its picture.");
                    Verify(frame, index);
                }
            }
            using MemoryStream output = new();
            VideoEncodingRequest request = new(0, 0, new H264EncoderFactory(), new VideoPreparationOptions
                { OutputPixelFormat = VideoPixelFormat.Yuv420P8, MaximumWidth = 32, RgbConversion = new() });
            MediaProcessingResult encoded = await engine.ProcessAsync(source, output, "mpegts", [request], allocator);
            if (encoded.Tracks[0].Frames != positions.Count || output.Length == 0)
            { throw new InvalidDataException("MPEG-4 Visual to AVC changed the picture count."); }
            int first = positions.Count / 4, end = positions.Count * 3 / 4;
            using MemoryStream interval = new();
            MediaProcessingResult selected = await engine.ProcessAsync(source, interval, "mpegts", [request], allocator,
                new() { Selection = new(positions[first], positions[end]) });
            if (selected.Tracks[0].Frames != end - first || interval.Length == 0)
            { throw new InvalidDataException("MPEG-4 Visual interval selection changed the picture count."); }
            if (allocator.AllocatedBytes != 0)
            { throw new InvalidDataException("MPEG-4 Visual processing retained buffer storage."); }

            void Verify(VideoFrame frame, int index)
            {
                if (frame.Timestamp is not MediaTimestamp time || time.CompareTo(new(index, cadence)) != 0 ||
                    frame.Duration is not MediaDuration duration || new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(1, cadence)) != 0)
                { throw new InvalidDataException("MPEG-4 Visual presentation time or duration differs from its exact clock."); }
                Mpeg4TestInput.VerifyPixels(frame, expected.AsSpan(checked(index * frameBytes), frameBytes));
            }
        }
    }
}
