// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Mediana.DrawingComparison
{
    internal static class ComparisonApplication
    {
        internal static async Task RunAsync(string[] args)
        {
            if (args.Length is not 10 and not 11) { throw new ArgumentException("Usage: <mediana|imagesharp> <scene> <width> <height> <complexity> <font-file> <iterations> <seconds> <parallelism> <export-directory|-> [scale]"); }
            int width = int.Parse(args[2], CultureInfo.InvariantCulture), height = int.Parse(args[3], CultureInfo.InvariantCulture);
            int complexity = int.Parse(args[4], CultureInfo.InvariantCulture), iterations = int.Parse(args[6], CultureInfo.InvariantCulture);
            double seconds = double.Parse(args[7], CultureInfo.InvariantCulture);
            int parallelism = int.Parse(args[8], CultureInfo.InvariantCulture);
            byte[] font = await File.ReadAllBytesAsync(args[5]);
            long allocated = GC.GetTotalAllocatedBytes(true), start = Stopwatch.GetTimestamp();
            IDrawingEngine engine = args[0] switch
            {
                "mediana" => new MedianaEngine(font),
                "imagesharp" => new ImageSharpEngine(font),
                _ => throw new ArgumentOutOfRangeException(nameof(args))
            };
            double fontTime = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long fontBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            ComparisonScene scene = new(args[1], width, height, complexity, args.Length == 11 ? int.Parse(args[10], CultureInfo.InvariantCulture) : 1);
            int running = 0, observed = 0;
            void Draw()
            {
                int concurrent = Interlocked.Increment(ref running), previous;
                do { previous = Volatile.Read(ref observed); }
                while (concurrent > previous && Interlocked.CompareExchange(ref observed, concurrent, previous) != previous);
                try { using IRenderedImage image = engine.Render(scene); }
                finally { Interlocked.Decrement(ref running); }
            }
            void Batch()
            {
                if (parallelism == 1) { Draw(); }
                else { Parallel.For(0, parallelism, new() { MaxDegreeOfParallelism = parallelism }, _ => Draw()); }
            }
            allocated = GC.GetTotalAllocatedBytes(true); start = Stopwatch.GetTimestamp();
            Draw();
            double cold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long coldBytes = GC.GetTotalAllocatedBytes(true) - allocated;
            Stopwatch warmup = Stopwatch.StartNew();
            int calls = 0;
            do { Batch(); calls++; } while (warmup.ElapsedMilliseconds < 500 || calls < 3);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            observed = 0;
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2), rounds = 0;
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpu = process.TotalProcessorTime;
            allocated = GC.GetTotalAllocatedBytes(true);
            Stopwatch elapsed = Stopwatch.StartNew();
            do { Batch(); rounds++; } while (rounds < iterations || elapsed.Elapsed.TotalSeconds < seconds);
            elapsed.Stop();
            long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
            double cpuTime = (process.TotalProcessorTime - cpu).TotalMilliseconds;
            process.Refresh();
            int count = rounds * parallelism;
            ComparisonMeasurement result = new(args[0], scene.Name, width, height, scene.Scale, complexity, Path.GetFileName(args[5]), Convert.ToHexString(SHA256.HashData(font)),
                Environment.ProcessorCount, parallelism, observed, count, elapsed.Elapsed.TotalSeconds, cold, coldBytes, fontTime, fontBytes,
                elapsed.Elapsed.TotalMilliseconds / count, cpuTime / count, bytes / count, process.PeakWorkingSet64, process.WorkingSet64,
                GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
            if (args[9] != "-")
            {
                Directory.CreateDirectory(args[9]);
                using IRenderedImage image = engine.Render(scene);
                byte[] pixels = new byte[checked(scene.PixelWidth * scene.PixelHeight * 4)];
                image.CopyPixelsTo(pixels);
                await File.WriteAllBytesAsync(Path.Combine(args[9], args[0] + ".rgba"), pixels);
            }
            Console.WriteLine(JsonSerializer.Serialize(result, ComparisonJsonContext.Default.ComparisonMeasurement));
        }
    }
}
