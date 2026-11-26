// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal interface IH264ResidualReader
    {
        /// <summary>Reads a residual block and sets unused coefficient positions to zero.</summary>
        int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients);
    }
}
