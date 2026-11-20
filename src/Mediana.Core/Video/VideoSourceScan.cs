// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Declared scan type of the source material, independent of coding or display cadence.</summary>
    public enum VideoSourceScan
    {
        Unknown,
        Progressive,
        Interlaced,
        Mixed
    }
}
