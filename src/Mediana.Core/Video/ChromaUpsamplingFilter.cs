// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Reconstruction kernels for subsampled color components.</summary>
    public enum ChromaUpsamplingFilter
    {
        NearestNeighbor,
        Bilinear,
        Bicubic,
        Lanczos3
    }
}
