// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal interface IH264ResidualReader
    {
        /// <summary>Reads a residual block and sets unused coefficient positions to zero.</summary>
        int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients);

        /// <summary>Reads coded luma into the inverse layout, or leaves the bit reader untouched if unavailable.</summary>
        bool TryReadPreparedLuma8x8(ref H264BitReader bits, int x, int y, int qp, scoped Span<int> coefficients,
            out H264ResidualSymmetry symmetry, out int dcLevel)
        {
            symmetry = H264ResidualSymmetry.None;
            dcLevel = 0;
            return false;
        }
    }
}
