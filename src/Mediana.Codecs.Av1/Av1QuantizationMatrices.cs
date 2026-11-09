// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1QuantizationMatrices
    {
        private const int PlaneSize = 3344;

        internal static ReadOnlySpan<byte> Weights(int level, int plane, Av1TransformSize size)
        {
            ReadOnlySpan<ushort> offsets = [0, 16, 80, 336, 336, 1360, 1392, 1424, 1552, 1680, 2192, 336, 336, 2704, 2768, 2832, 3088, 1680, 2192];
            int planeOffset = plane switch
            {
                0 => 0,
                1 or 2 => PlaneSize,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            ReadOnlySpan<byte> values = level switch
            {
                0 => Level00,
                1 => Level01,
                2 => Level02,
                3 => Level03,
                4 => Level04,
                5 => Level05,
                6 => Level06,
                7 => Level07,
                8 => Level08,
                9 => Level09,
                10 => Level10,
                11 => Level11,
                12 => Level12,
                13 => Level13,
                14 => Level14,
                _ => throw new ArgumentOutOfRangeException(nameof(level))
            };
            return values.Slice(planeOffset + offsets[size.Index], size.CodedWidth * size.CodedHeight);
        }
    }
}
