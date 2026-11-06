// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.DrawingBenchmarks
{
    internal record DrawingMeasurement(string Scene, int Width, int Height, int Scale, int Iterations,
        double RenderMilliseconds, double DownsampleMilliseconds, double EncodeMilliseconds,
        double CpuMilliseconds, long RenderAllocatedBytes, long DownsampleAllocatedBytes,
        long EncodeAllocatedBytes, long PeakWorkingSetBytes, int EncodedBytes, string PixelHash);
}
