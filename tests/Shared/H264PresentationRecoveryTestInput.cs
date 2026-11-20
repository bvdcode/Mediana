// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class H264PresentationRecoveryTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            await using FileStream compressed = File.OpenRead(Path.Combine(root, "avc-interlaced.yuv.gz"));
            await using GZipStream decoded = new(compressed, CompressionMode.Decompress);
            using MemoryStream expected = new();
            await decoded.CopyToAsync(expected);
            byte[] samples = expected.ToArray();
            if (samples.Length != 96 * 80 * 3 / 2)
            {
                throw new InvalidDataException("The independent presentation reference is incomplete.");
            }
            List<string> warnings = [];
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory(new() { IgnoreInconsistentPictureTiming = true }, warnings.Add)).Build();
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "avc-interlaced.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, allocator, new(), 0);
            using VideoFrame retained = await session.ReadFrameAsync() ?? throw new InvalidDataException("The presentation picture is missing.");
            VerifyFrame(retained, samples);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                using VideoFrame? extra = await session.ReadFrameAsync();
                if (extra is not null)
                {
                    throw new InvalidDataException("Presentation recovery duplicated a decoded picture.");
                }
                MediaTimestamp target = new(0, new TimeBase(1, 25));
                await session.SeekAsync(target);
                using VideoFrame repeated = await session.ReadFrameAtOrAfterAsync(target)
                    ?? throw new InvalidDataException("The recovered picture is missing after seeking.");
                VerifyFrame(repeated, samples);
            }
            if (warnings.Count != 1 || !warnings[0].Contains("picture-timing", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Presentation recovery must report one warning per decoder, including repeated seeks.");
            }
            await session.DisposeAsync();
            VerifyFrame(retained, samples);
            retained.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Presentation recovery retained media buffers.");
            }
        }

        private static void VerifyFrame(VideoFrame frame, ReadOnlySpan<byte> expected)
        {
            if (frame.Format.Width != 96 || frame.Format.Height != 80 || frame.Format.PixelFormat != VideoPixelFormat.Yuv420P8 ||
                frame.Format.ScanMode != VideoScanMode.BottomFieldFirst ||
                frame.Presentation != VideoFramePresentation.Fields(VideoFieldParity.Bottom, 2) ||
                frame.Timestamp is not MediaTimestamp timestamp || timestamp.CompareTo(new(0, new TimeBase(1, 25))) != 0 ||
                frame.Duration is not MediaDuration duration ||
                new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(1, new TimeBase(1, 25))) != 0)
            {
                throw new InvalidDataException("Recovered presentation changed the coded geometry, order or packet timing.");
            }
            H264TestInput.VerifyFrame(frame, expected);
        }
    }
}
