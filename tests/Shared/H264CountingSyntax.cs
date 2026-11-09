// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal readonly struct H264CountingSyntax : IH264EncodingSyntax
    {
        public void WriteIntraMode(int mode, int predicted) => throw new NotSupportedException();
        public bool TryWriteResidual(H264ResidualCategory category, int plane, int x, int y, ReadOnlySpan<int> coefficients, out int count)
        {
            count = 0; foreach (int value in coefficients) { if (value != 0) { count++; } }
            return true;
        }
    }
}
