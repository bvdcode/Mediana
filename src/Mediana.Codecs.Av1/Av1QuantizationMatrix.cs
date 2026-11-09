// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1QuantizationMatrix(bool Enabled, byte Y, byte U, byte V)
    {
        internal ReadOnlySpan<byte> Weights(int plane, Av1TransformSize size, int transform, bool lossless)
        {
            if (!Enabled || lossless || transform >= 9) { return default; }
            int level = plane switch
            {
                0 => Y,
                1 => U,
                2 => V,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            if (level == 15) { return default; }
            return Av1QuantizationMatrices.Weights(level, plane, size);
        }

        internal static Av1QuantizationMatrix Read(ref Av1BitReader bits, bool separateUv)
        {
            if (!bits.Flag()) { return default; }
            byte y = (byte)bits.Read(4);
            byte u = (byte)bits.Read(4);
            byte v = separateUv ? (byte)bits.Read(4) : u;
            return new(true, y, u, v);
        }
    }
}
