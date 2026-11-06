// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.DrawingBenchmarks
{
    internal record DrawingBatchMeasurement(string Scene, int Size, int Scale, int Parallelism, int ObservedParallelism, int Iterations,
        double ColdMilliseconds, long ColdAllocatedBytes, double MillisecondsPerImage, double CpuMillisecondsPerImage,
        long AllocatedBytesPerImage, long PeakWorkingSetBytes, int Gen0Collections, int Gen1Collections, int Gen2Collections, string PixelHash);
}
