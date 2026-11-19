// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.CompressionBenchmarks
{
    internal static class Program
    {
        private static Task Main(string[] args) => CompressionBenchmark.RunAsync(args);
    }
}
