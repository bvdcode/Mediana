// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.DrawingComparison
{
    internal record ComparisonMeasurement(string Engine, string Scene, int Width, int Height, int Scale, int Complexity, string Font, string FontHash,
        int ProcessorCount, int Parallelism, int ObservedParallelism, int Operations, double Seconds,
        double ColdMilliseconds, long ColdAllocatedBytes, double FontLoadMilliseconds, long FontLoadAllocatedBytes,
        double ElapsedMillisecondsPerImage, double CpuMillisecondsPerImage, long ManagedAllocatedBytesPerImage,
        long PeakWorkingSetBytes, long WorkingSetBytes, int Gen0, int Gen1, int Gen2);
}
