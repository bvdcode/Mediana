// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.ProcessingBenchmarks
{
    internal static class ProcessingBenchmarkApplication
    {
        public static Task RunAsync(string[] args)
        {
            if (args.Length == 0) { ScalingBenchmark.Run(); return Task.CompletedTask; }
            if (args[0] != "pipeline") { throw new ArgumentException("Use pipeline input.mp4 output.ts width height [start-ms end-ms], or no arguments for scaling."); }
            return PipelineBenchmark.RunAsync(args[1..]);
        }
    }
}
