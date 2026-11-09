// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1
{
    internal static class Av1CdefKernel
    {
        internal static void Filter<T>(ReadOnlySpan<T> source, Span<T> output, int stride, int planeHeight,
            int x0, int y0, int side, int primary, int secondary, int damping, int direction, int bitDepth = 8, int? lumaVariance = null) where T : unmanaged, IBinaryInteger<T>
        {
            int coefficientShift = bitDepth switch
            {
                8 => 0,
                10 => 2,
                12 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            };
            primary <<= coefficientShift;
            secondary <<= coefficientShift;
            damping += coefficientShift;
            if (lumaVariance is int variance)
            {
                int varianceStrength = variance >> 6;
                int adjustment = varianceStrength == 0 ? 0 : Math.Min(BitOperations.Log2((uint)varianceStrength), 12);
                primary = variance == 0 ? 0 : (primary * (4 + adjustment) + 8) >> 4;
            }
            int primaryParity = (primary >> coefficientShift) & 1;
            if (primary == 0 && secondary == 0) { return; }
            int primaryShift = primary == 0 ? 0 : Math.Max(0, damping - BitOperations.Log2((uint)primary));
            int secondaryShift = secondary == 0 ? 0 : Math.Max(0, damping - BitOperations.Log2((uint)secondary));
            ReadOnlySpan<sbyte> rows = [-1, -2, 0, -1, 0, 0, 0, 1, 1, 2, 1, 2, 1, 2, 1, 2];
            ReadOnlySpan<sbyte> columns = [1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 0, 1, 0, 0, 0, -1];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int value = int.CreateTruncating(source[(y + 2) * stride + x0 + x]);
                    int minimum = value;
                    int maximum = value;
                    int sum = 0;
                    for (int tap = 0; tap < 2; tap++)
                    {
                        int primaryTap = primaryParity != 0 ? 3 : tap == 0 ? 4 : 2;
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            Add(source, stride, planeHeight, x0 + x, y0 + y, y + 2, rows[direction * 2 + tap] * sign,
                                columns[direction * 2 + tap] * sign, value, primary, primaryShift, primaryTap, ref sum, ref minimum, ref maximum);
                            for (int difference = -2; difference <= 2; difference += 4)
                            {
                                int other = (direction + difference) & 7;
                                Add(source, stride, planeHeight, x0 + x, y0 + y, y + 2, rows[other * 2 + tap] * sign,
                                    columns[other * 2 + tap] * sign, value, secondary, secondaryShift, 2 - tap, ref sum, ref minimum, ref maximum);
                            }
                        }
                    }
                    output[(y0 + y) * stride + x0 + x] = T.CreateTruncating(Math.Clamp(value + ((8 + sum - (sum < 0 ? 1 : 0)) >> 4), minimum, maximum));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Add<T>(ReadOnlySpan<T> source, int stride, int height, int x, int y, int localY, int dy, int dx,
            int value, int strength, int shift, int tap, ref int sum, ref int minimum, ref int maximum) where T : unmanaged, IBinaryInteger<T>
        {
            if ((uint)(x + dx) >= stride || (uint)(y + dy) >= height) { return; }
            int sample = int.CreateTruncating(source[(localY + dy) * stride + x + dx]);
            int difference = sample - value;
            int magnitude = Math.Abs(difference);
            int constrained = Math.Min(magnitude, Math.Max(0, strength - (magnitude >> shift)));
            sum += tap * (difference < 0 ? -constrained : constrained);
            minimum = Math.Min(minimum, sample);
            maximum = Math.Max(maximum, sample);
        }
    }
}
