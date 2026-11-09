// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal interface IH264EncodingSyntax
    {
        void WriteIntraMode(int mode, int predicted);
        bool TryWriteResidual(H264ResidualCategory category, int plane, int x, int y,
            ReadOnlySpan<int> coefficients, out int count);
    }
}
