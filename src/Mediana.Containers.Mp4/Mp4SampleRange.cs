// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    /// <summary>A contiguous decode-order sample range in one source track. Indices are zero-based.</summary>
    public readonly record struct Mp4SampleRange(int StreamIndex, int FirstSample, int SampleCount);
}
