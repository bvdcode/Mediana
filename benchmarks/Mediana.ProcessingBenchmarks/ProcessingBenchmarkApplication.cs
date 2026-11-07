// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.ProcessingBenchmarks
{
    internal static class ProcessingBenchmarkApplication
    {
        public static Task RunAsync(string[] args)
        {
            if (args.Length == 0) { ScalingBenchmark.Run(); return Task.CompletedTask; }
            return args[0] switch
            {
                "pipeline" => PipelineBenchmark.RunAsync(args[1..]),
                "hls" => HlsBenchmark.RunAsync(args[1..]),
                _ => throw new ArgumentException("Use pipeline input.mp4 output.ts width height [start-ms end-ms], hls input.mp4 output-directory width height segment-number|all, or no arguments for scaling.")
            };
        }
    }
}
