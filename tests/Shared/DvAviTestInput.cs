// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.Dv;
using Mediana.Containers.Avi;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class DvAviTestInput
    {
        private static readonly TimeBase Clock = new(1, 25);
        private const int FrameSize = 720 * 576 * 3 / 2;

        internal static async Task VerifyAsync(string root)
        {
            byte[] expected = new byte[FrameSize * 2];
            using (FileStream file = File.OpenRead(Path.Combine(root, "first-and-fourth.yuv.gz")))
            {
                await using GZipStream archive = new(file, CompressionMode.Decompress);
                await archive.ReadExactlyAsync(expected);
            }
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new AviReaderFactory())
                .AddVideoDecoder(new DvDecoderFactory()).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "pal.avi")));
            await using (VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), null))
            {
                if (!session.CanSeek || session.Stream.CodecId != DvFrameHeader.CodecId || session.Stream.TimeBase != Clock)
                {
                    throw new InvalidDataException("The AVI session did not expose seekable DV video at 25 fps.");
                }
                using (VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("Missing first DV frame."))
                {
                    VerifyFrame(frame, 0, expected.AsSpan(0, FrameSize));
                }
                MediaTimestamp target = new(119, new(1, 1000));
                await session.SeekAsync(target);
                using (VideoFrame frame = await session.ReadFrameAtOrAfterAsync(target) ?? throw new InvalidDataException("Missing sought DV frame."))
                {
                    VerifyFrame(frame, 3, expected.AsSpan(FrameSize));
                }
                MediaTimestamp start = new(0, Clock);
                await session.SeekAsync(start);
                using VideoFrame repeated = await session.ReadFrameAtOrAfterAsync(start) ?? throw new InvalidDataException("Missing DV frame after seeking back.");
                VerifyFrame(repeated, 0, expected.AsSpan(0, FrameSize));
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("The DV session retained storage after disposal.");
            }
        }

        private static void VerifyFrame(VideoFrame frame, int frameIndex, ReadOnlySpan<byte> expected)
        {
            if (frame.Format.Width != 720 || frame.Format.Height != 576 || frame.Format.PixelFormat != VideoPixelFormat.Yuv420P8 ||
                frame.Format.SampleAspectRatio != SampleAspectRatio.Square ||
                frame.Timestamp != new MediaTimestamp(frameIndex, Clock) || frame.Duration != new MediaDuration(1, Clock))
            {
                throw new InvalidDataException("The DV frame geometry or timing differs from the reference.");
            }
            int maximum = 0;
            int at = 0;
            for (int plane = 0; plane < frame.Planes.Count; plane++)
            {
                for (int y = 0; y < frame.Planes[plane].Height; y++)
                {
                    foreach (byte value in frame.GetRow(plane, y))
                    {
                        maximum = Math.Max(maximum, Math.Abs(value - expected[at++]));
                    }
                }
            }
            if (at != expected.Length || maximum > 1)
            {
                throw new InvalidDataException($"DV samples differ from reference, maximum difference {maximum}.");
            }
        }
    }
}
