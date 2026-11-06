// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.DrawingBenchmarks
{
    internal static class DrawingApplication
    {
        internal static void Run(string[] args)
        {
            if (args.Length > 0 && args[0] == "batch") { DrawingBatchBenchmark.Run(args[1..]); }
            else { DrawingBenchmark.Run(args); }
        }
    }
}
