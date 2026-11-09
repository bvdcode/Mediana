// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1MaskSampling
    {
        internal static int Weight(ReadOnlySpan<byte> mask, int stride, int sub, int row, int column)
        {
            int position = (row << sub) * stride + (column << sub);
            return sub switch
            {
                0 => mask[position],
                1 => (mask[position] + mask[position + 1] + mask[position + stride] + mask[position + stride + 1] + 2) >> 2,
                _ => throw new InvalidDataException("The AV1 mask subsampling is invalid.")
            };
        }
    }
}
