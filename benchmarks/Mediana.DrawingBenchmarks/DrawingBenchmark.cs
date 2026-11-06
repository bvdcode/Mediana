// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.DrawingBenchmarks
{
    internal static class DrawingBenchmark
    {
        internal static void Run(string[] args)
        {
            if (args.Length != 6) { throw new ArgumentException("Usage: <text|bars|curves|strokes|tiles> <width> <height> <scale> <iterations; 0=auto> <output-directory>"); }
            int width = int.Parse(args[1], CultureInfo.InvariantCulture);
            int height = int.Parse(args[2], CultureInfo.InvariantCulture);
            int scale = int.Parse(args[3], CultureInfo.InvariantCulture);
            int iterations = int.Parse(args[4], CultureInfo.InvariantCulture);
            DrawingWorkload workload = new(args[0], width, height, scale);
            Directory.CreateDirectory(args[5]);
            MediaBufferAllocator allocator = new(long.MaxValue);
            double render = 0, resize = 0, encode = 0;
            long renderBytes = 0, resizeBytes = 0, encodeBytes = 0;
            string hash = string.Empty;
            int outputBytes = 0;
            void Once(bool export)
            {
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                using PixelBuffer pixels = new(width * scale, height * scale, allocator);
                workload.Draw(new(pixels));
                render += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                renderBytes += GC.GetAllocatedBytesForCurrentThread() - allocation;
                allocation = GC.GetAllocatedBytesForCurrentThread();
                start = Stopwatch.GetTimestamp();
                using PixelBuffer? downsampled = scale == 1 ? null : new PixelBuffer(width, height, allocator);
                if (downsampled is not null) { pixels.DownsampleTo(downsampled, scale); }
                PixelBuffer image = downsampled ?? pixels;
                resize += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                resizeBytes += GC.GetAllocatedBytesForCurrentThread() - allocation;
                if (export)
                {
                    hash = Convert.ToHexString(SHA256.HashData(image.Pixels.Span));
                    File.WriteAllBytes(Path.Combine(args[5], "output.rgba"), image.Pixels.ToArray());
                }
                allocation = GC.GetAllocatedBytesForCurrentThread();
                start = Stopwatch.GetTimestamp();
                using VideoFrame frame = image.PublishVideoFrame();
                using MediaBuffer encoded = WebpEncoder.Encode(frame, allocator);
                encode += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                encodeBytes += GC.GetAllocatedBytesForCurrentThread() - allocation;
                outputBytes = encoded.Memory.Length;
                if (export) { File.WriteAllBytes(Path.Combine(args[5], "output.webp"), encoded.Memory.ToArray()); }
            }
            Stopwatch warmup = Stopwatch.StartNew();
            int calls = 0;
            do { Once(false); calls++; } while (warmup.ElapsedMilliseconds < 500 || calls < 3);
            warmup.Stop();
            if (iterations == 0) { iterations = Math.Clamp((int)(1500 / Math.Max(1, warmup.Elapsed.TotalMilliseconds / calls)), 3, 200); }
            Once(true);
            render = resize = encode = 0;
            renderBytes = resizeBytes = encodeBytes = 0;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpu = process.TotalProcessorTime;
            for (int iteration = 0; iteration < iterations; iteration++) { Once(false); }
            TimeSpan elapsedCpu = process.TotalProcessorTime - cpu;
            if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Drawing retained live pixel storage."); }
            process.Refresh();
            DrawingMeasurement result = new(args[0], width, height, scale, iterations,
                render / iterations, resize / iterations, encode / iterations, elapsedCpu.TotalMilliseconds / iterations,
                renderBytes / iterations, resizeBytes / iterations, encodeBytes / iterations,
                process.PeakWorkingSet64, outputBytes, hash);
            Console.WriteLine(JsonSerializer.Serialize(result, DrawingJsonContext.Default.DrawingMeasurement));
        }
    }
}
