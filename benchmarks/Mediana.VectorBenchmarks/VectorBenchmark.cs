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

namespace Mediana.VectorBenchmarks
{
    internal static class VectorBenchmark
    {
        internal static async Task RunAsync(string[] args)
        {
            if (args.Length != 8) { throw new ArgumentException("Usage: <input> <size> <supersampling> <position 0..1> <rounds> <concurrency> <render|preview> <export-directory>"); }
            int size = int.Parse(args[1], CultureInfo.InvariantCulture), scale = int.Parse(args[2], CultureInfo.InvariantCulture);
            double position = double.Parse(args[3], CultureInfo.InvariantCulture);
            int rounds = int.Parse(args[4], CultureInfo.InvariantCulture), concurrency = int.Parse(args[5], CultureInfo.InvariantCulture);
            bool preview = args[6] switch { "render" => false, "preview" => true, _ => throw new ArgumentException("Unknown mode.") };
            ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
            if (!double.IsFinite(position) || position < 0 || position >= 1) { throw new ArgumentOutOfRangeException(nameof(args)); }
            using VectorWorkload workload = await VectorWorkload.ReadAsync(args[0], size, scale, position);
            long start = Stopwatch.GetTimestamp();
            using PixelBuffer first = await workload.RenderAsync(preview);
            double firstTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            byte[] expected = first.Pixels.ToArray();
            start = Stopwatch.GetTimestamp();
            using MediaBuffer? firstWebp = preview ? Encode(first) : null;
            firstTime += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            string hash = Convert.ToHexString(SHA256.HashData(expected));
            string webpHash = firstWebp is null ? string.Empty : Convert.ToHexString(SHA256.HashData(firstWebp.Memory.Span));
            int width = first.Width, height = first.Height;
            Directory.CreateDirectory(args[7]);
            await File.WriteAllBytesAsync(Path.Combine(args[7], "output.rgba"), expected);
            if (firstWebp is not null) { await File.WriteAllBytesAsync(Path.Combine(args[7], "output.webp"), firstWebp.Memory.ToArray()); }
            firstWebp?.Dispose();
            first.Dispose();
            async Task OnceAsync()
            {
                using PixelBuffer image = await workload.RenderAsync(preview);
                if (!image.Pixels.Span.SequenceEqual(expected)) { throw new InvalidDataException("Repeated or concurrent rendering changed pixels."); }
                using MediaBuffer? webp = preview ? Encode(image) : null;
                if (webp is not null && Convert.ToHexString(SHA256.HashData(webp.Memory.Span)) != webpHash)
                { throw new InvalidDataException("Repeated or concurrent encoding changed bytes."); }
            }
            async Task BatchAsync()
            {
                if (concurrency == 1) { await OnceAsync(); }
                else { await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => Task.Run(OnceAsync))); }
            }
            Stopwatch warmup = Stopwatch.StartNew();
            int calls = 0;
            do { await BatchAsync(); calls++; } while (calls < 3 || warmup.ElapsedMilliseconds < 500);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            double[] times = new double[rounds];
            int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
            using Process process = Process.GetCurrentProcess();
            long allocation = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            for (int round = 0; round < rounds; round++)
            {
                start = Stopwatch.GetTimestamp();
                await BatchAsync();
                times[round] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            double cpuTime = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            long bytes = GC.GetTotalAllocatedBytes(true) - allocation;
            for (int generation = 0; generation < collections.Length; generation++) { collections[generation] = GC.CollectionCount(generation) - collections[generation]; }
            Array.Sort(times);
            process.Refresh();
            VectorMeasurement measurement = new(Path.GetFileName(args[0]), args[6], size, scale, position, concurrency, rounds,
                width, height, firstTime, times[rounds / 2], times.Sum() / (rounds * concurrency), cpuTime / (rounds * concurrency),
                bytes / (rounds * concurrency), process.PeakWorkingSet64, collections, hash, webpHash);
            Console.WriteLine(JsonSerializer.Serialize(measurement, VectorJsonContext.Default.VectorMeasurement));
        }

        private static MediaBuffer Encode(PixelBuffer image)
        {
            using VideoFrame frame = image.PublishVideoFrame();
            return WebpEncoder.Encode(frame, new MediaBufferAllocator(long.MaxValue));
        }
    }
}
