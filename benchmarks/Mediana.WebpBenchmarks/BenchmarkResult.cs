// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.WebpBenchmarks
{
    internal record BenchmarkResult(string Encoder, string Sample, int Width, int Height, int Iterations,
        double WallMilliseconds, double CpuMilliseconds, long AllocatedBytes, long PeakWorkingSetBytes, long EncodedBytes);
}
