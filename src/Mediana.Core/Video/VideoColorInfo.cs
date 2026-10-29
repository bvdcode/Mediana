// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    public record VideoColorInfo(
        ColorPrimaries Primaries = ColorPrimaries.Unspecified,
        TransferFunction Transfer = TransferFunction.Unspecified,
        MatrixCoefficients Matrix = MatrixCoefficients.Unspecified,
        ColorRange Range = ColorRange.Unspecified,
        ChromaLocation ChromaLocation = ChromaLocation.Unspecified);
}
