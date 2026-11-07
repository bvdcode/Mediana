// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Security.Cryptography;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.ProcessingBenchmarks
{
    internal static class ScalingBenchmark
    {
        private const int WarmupFrames = 30;
        private const int MeasuredFrames = 120;

        public static void Run()
        {
            foreach ((int width, int height, int targetWidth, int targetHeight) in new[]
            {
                (1920, 1080, 1280, 720), (1920, 1080, 640, 360), (3840, 2160, 640, 360),
                (1280, 720, 854, 480), (1280, 720, 1280, 720)
            })
            {
                Measure(width, height, targetWidth, targetHeight);
            }
        }

        private static void Measure(int width, int height, int targetWidth, int targetHeight)
        {
            MediaBufferAllocator allocator = new(128 * 1024 * 1024);
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8, new(ChromaLocation: ChromaLocation.Left));
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            new Random(456).NextBytes(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(format, buffer, planes);
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.Create(format, targetWidth, targetHeight, allocator);
            for (int index = 0; index < WarmupFrames; index++)
            {
                using VideoFrame warm = resizer.Apply(frame);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using Process process = Process.GetCurrentProcess();
            long bytes = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch watch = Stopwatch.StartNew();
            long peakBuffers = allocator.AllocatedBytes;
            byte[]? result = null;
            for (int index = 0; index < MeasuredFrames; index++)
            {
                using VideoFrame output = resizer.Apply(frame);
                peakBuffers = Math.Max(peakBuffers, allocator.AllocatedBytes);
                if (index == MeasuredFrames - 1) { result = output.Data.ToArray(); }
            }
            watch.Stop();
            cpu = process.TotalProcessorTime - cpu;
            long allocated = GC.GetTotalAllocatedBytes(true) - bytes;
            Console.WriteLine(FormattableString.Invariant(
                $"{width}x{height}->{targetWidth}x{targetHeight}: ms/frame={watch.Elapsed.TotalMilliseconds / MeasuredFrames:F3}; cpu-ms/frame={cpu.TotalMilliseconds / MeasuredFrames:F3}; allocated-bytes/frame={allocated / MeasuredFrames}; peak-buffer-bytes={peakBuffers}; sha256={Convert.ToHexString(SHA256.HashData(result!))}"));
        }
    }
}
