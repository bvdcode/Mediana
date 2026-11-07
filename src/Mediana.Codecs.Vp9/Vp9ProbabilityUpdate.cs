// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9ProbabilityUpdate
    {
        internal static void Diff(ref Vp9BooleanReader bits, Span<byte> probabilities)
        {
            for (int i = 0; i < probabilities.Length; i++)
            {
                if (bits.Read(252)) { probabilities[i] = Remap(Subexponential(ref bits), probabilities[i]); }
            }
        }

        internal static void Motion(ref Vp9BooleanReader bits, Span<byte> probabilities)
        {
            for (int i = 0; i < probabilities.Length; i++)
            { if (bits.Read(252)) { probabilities[i] = (byte)((bits.Literal(7) << 1) | 1); } }
        }

        internal static byte Remap(int delta, int probability)
        {
            int value = Vp9Tables.InverseProbabilityMap[delta], center = probability - 1;
            return (byte)((center << 1) <= 255 ? 1 + Recenter(value, center) : 255 - Recenter(value, 254 - center));
        }

        private static int Recenter(int value, int center) => value > 2 * center ? value :
            (value & 1) != 0 ? center - ((value + 1) >> 1) : center + (value >> 1);

        private static int Subexponential(ref Vp9BooleanReader bits)
        {
            if (!bits.Read()) { return bits.Literal(4); }
            if (!bits.Read()) { return bits.Literal(4) + 16; }
            if (!bits.Read()) { return bits.Literal(5) + 32; }
            int value = bits.Literal(7);
            return value < 65 ? value + 64 : (value << 1) - 1 + bits.Literal(1);
        }
    }
}
