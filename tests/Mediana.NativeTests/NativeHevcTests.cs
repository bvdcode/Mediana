// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Hevc;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;
using Mediana.Tests;

namespace Mediana.NativeTests
{
    internal static class NativeHevcTests
    {
        public static async Task VerifyAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Hevc");
            int frames = 0;
            foreach (string name in new[] { "main8", "main10", "weighted8", "weighted10", "gop8", "gop10", "slices8", "predictive10", "intra8" })
            { frames += await HevcDecoderTestInput.VerifyAsync(root, name); }
            if (frames != 208) { throw new InvalidDataException("The public HEVC frame reference count differs."); }
            await HevcSeekTestInput.VerifyAsync(root, "main8", 12, 12, ".mkv");
            await HevcSeekTestInput.VerifyAsync(root, "main10", 12, 12, ".mkv");
            await HevcSeekTestInput.VerifyAsync(root, "gop8", 48, 12);
            await HevcSeekTestInput.VerifyAsync(root, "gop10", 48, 12);
            await HevcSeekTestInput.VerifyAsync(root, "slices8", 12, 6);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory()).AddVideoDecoder(new HevcDecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(root, "gop10.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException("HEVC preview input is empty."))
            using (VideoFrame preview = SrgbPreview.Fit(frame, VideoDisplayMatrix.Identity, 100, 100, allocator))
            {
                if (frame.Format.PixelFormat != VideoPixelFormat.Yuv420P10 || preview.Format.PixelFormat != VideoPixelFormat.Rgba32 ||
                    preview.Format.Width != 100 || preview.Format.Height != 67 || preview.Data.Span.IndexOfAnyExcept((byte)0) < 0)
                { throw new InvalidDataException("The HEVC Main10 SDR preview differs."); }
            }
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0) { throw new InvalidDataException("The HEVC preview retained pixel storage."); }
            Console.WriteLine("PASS public HEVC: 208 exact Main/Main10 I/P/B frames, open/closed GOP, independent slices, MP4/Matroska seeking, timing, pixel leases and ten-bit SDR preview.");
        }
    }
}
