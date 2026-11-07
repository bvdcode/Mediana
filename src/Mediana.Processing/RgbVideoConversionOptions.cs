// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Explicit matrix/range for converting opaque RGB samples to centered YUV420P8.
    /// Primaries and transfer are retained; no compositing, gamut conversion or tone mapping is implied.</summary>
    public record RgbVideoConversionOptions
    {
        public MatrixCoefficients Matrix { get; init; } = MatrixCoefficients.Bt709;
        public ColorRange Range { get; init; } = ColorRange.Limited;
    }
}
