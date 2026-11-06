// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    /// <summary>Resolution and surface limits for rasterizing point-based document pages.</summary>
    public record PageRasterOptions
    {
        public double Dpi { get; init; } = 96;
        public int? MaximumWidth { get; init; }
        public int? MaximumHeight { get; init; }
        public int Supersampling { get; init; } = 2;
        public int MaximumSamples { get; init; } = 16_777_216;
    }
}
