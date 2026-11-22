// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.VectorBenchmarks
{
    internal record VectorMeasurement(string Input, string Mode, int Size, int Scale, double Position, int Concurrency, int Rounds,
        int Width, int Height, double FirstCallMilliseconds, double MedianBatchMilliseconds, double ElapsedMillisecondsPerImage,
        double CpuMillisecondsPerImage, long AllocatedBytesPerImage, long PeakWorkingSetBytes, int[] Collections,
        string PixelHash, string WebpHash);
}
