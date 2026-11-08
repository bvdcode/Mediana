// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1GrainScaling
    {
        internal const int Length = 256;

        internal static void Initialize(ReadOnlySpan<Av1GrainPoint> points, Span<short> table)
        {
            if (points.IsEmpty) { table.Clear(); return; }
            table[..points[0].Value].Fill(points[0].Scaling);
            for (int index = 0; index < points.Length - 1; index++)
            {
                Av1GrainPoint start = points[index], end = points[index + 1];
                int width = end.Value - start.Value;
                int slope = (end.Scaling - start.Scaling) * ((65536 + (width >> 1)) / width);
                for (int x = 0; x < width; x++)
                {
                    table[start.Value + x] = (short)(start.Scaling + ((x * slope + 32768) >> 16));
                }
            }
            table[points[^1].Value..].Fill(points[^1].Scaling);
        }

        internal static int At(ReadOnlySpan<short> table, int value, int bitDepth)
        {
            int shift = bitDepth - 8;
            int index = value >> shift;
            if (shift == 0 || index == 255) { return table[index]; }
            int remainder = value - (index << shift);
            return table[index] + Av1GrainNoise.Round((table[index + 1] - table[index]) * remainder, shift);
        }
    }
}
