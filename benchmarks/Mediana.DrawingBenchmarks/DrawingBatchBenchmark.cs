// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Drawing;

namespace Mediana.DrawingBenchmarks
{
    internal static class DrawingBatchBenchmark
    {
        internal static void Run(string[] args)
        {
            if (args.Length != 5) { throw new ArgumentException("Usage: batch <scene> <size> <scale> <parallelism> <iterations>"); }
            int size = int.Parse(args[1], CultureInfo.InvariantCulture), scale = int.Parse(args[2], CultureInfo.InvariantCulture);
            int parallelism = int.Parse(args[3], CultureInfo.InvariantCulture), iterations = int.Parse(args[4], CultureInfo.InvariantCulture);
            DrawingWorkload workload = new(args[0], size, size, scale);
            string Draw()
            {
                using PixelBuffer pixels = new(size * scale, size * scale);
                workload.Draw(new(pixels));
                using PixelBuffer? output = scale == 1 ? null : pixels.Downsample(scale);
                return Convert.ToHexString(SHA256.HashData((output ?? pixels).Pixels.Span));
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            string hash = Draw();
            double cold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long coldBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            int running = 0, observed = 0;
            for (int i = 0; i < 3; i++) { Batch(); }
            observed = 0;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpu = process.TotalProcessorTime;
            bytes = GC.GetTotalAllocatedBytes(true);
            start = Stopwatch.GetTimestamp();
            for (int round = 0; round < iterations; round++) { Batch(); }
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetTotalAllocatedBytes(true) - bytes;
            double elapsedCpu = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            process.Refresh();
            int count = parallelism * iterations;
            DrawingBatchMeasurement result = new(args[0], size, scale, parallelism, observed, iterations, cold, coldBytes,
                elapsed / count, elapsedCpu / count, allocated / count, process.PeakWorkingSet64,
                GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, hash);
            Console.WriteLine(JsonSerializer.Serialize(result, DrawingJsonContext.Default.DrawingBatchMeasurement));
            void Batch()
            {
                if (parallelism == 1) { Verify(); }
                else { Parallel.For(0, parallelism, new() { MaxDegreeOfParallelism = parallelism }, _ => Verify()); }
            }
            void Verify()
            {
                int concurrent = Interlocked.Increment(ref running);
                int previous;
                do { previous = Volatile.Read(ref observed); }
                while (concurrent > previous && Interlocked.CompareExchange(ref observed, concurrent, previous) != previous);
                try
                {
                    if (Draw() != hash) { throw new InvalidDataException("Independent drawing contexts produced different pixels."); }
                }
                finally { Interlocked.Decrement(ref running); }
            }
        }
    }
}
