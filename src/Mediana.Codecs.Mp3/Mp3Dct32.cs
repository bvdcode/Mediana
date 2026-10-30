// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3Dct32
    {
        private static readonly double[] Factors = CreateFactors();

        public static void Transform(Span<double> values)
        {
            Span<double> scratch = stackalloc double[32];
            ForwardThirtyTwo(values[..32], scratch);
        }

        private static void ForwardThirtyTwo(Span<double> values, Span<double> scratch)
        {
            Fold(values, scratch, 16);
            ForwardSixteen(scratch[..16], values[..16]);
            ForwardSixteen(scratch[16..32], values[16..32]);
            Interleave(values, scratch, 16);
        }

        private static void ForwardSixteen(Span<double> values, Span<double> scratch)
        {
            Fold(values, scratch, 8);
            ForwardEight(scratch[..8]);
            ForwardEight(scratch[8..16]);
            Interleave(values, scratch, 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ForwardEight(Span<double> values)
        {
            double first = values[0] + values[7];
            double second = values[1] + values[6];
            double third = values[2] + values[5];
            double fourth = values[3] + values[4];
            double fifth = (values[0] - values[7]) * Factors[4];
            double sixth = (values[1] - values[6]) * Factors[5];
            double seventh = (values[2] - values[5]) * Factors[6];
            double eighth = (values[3] - values[4]) * Factors[7];
            ForwardFour(ref first, ref second, ref third, ref fourth);
            ForwardFour(ref fifth, ref sixth, ref seventh, ref eighth);
            values[0] = first;
            values[1] = fifth + sixth;
            values[2] = second;
            values[3] = sixth + seventh;
            values[4] = third;
            values[5] = seventh + eighth;
            values[6] = fourth;
            values[7] = eighth;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Fold(Span<double> values, Span<double> scratch, int half)
        {
            for (int index = 0; index < half; index++)
            {
                double first = values[index];
                double last = values[2 * half - index - 1];
                scratch[index] = first + last;
                scratch[half + index] = (first - last) * Factors[half + index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Interleave(Span<double> values, Span<double> scratch, int half)
        {
            for (int index = 0; index < half - 1; index++)
            {
                values[2 * index] = scratch[index];
                values[2 * index + 1] = scratch[half + index] + scratch[half + index + 1];
            }
            values[2 * half - 2] = scratch[half - 1];
            values[2 * half - 1] = scratch[2 * half - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ForwardFour(ref double value0, ref double value1, ref double value2, ref double value3)
        {
            double first = value0 + value3;
            double second = value1 + value2;
            double third = (value0 - value3) * 0.541196100146197;
            double fourth = (value1 - value2) * 1.3065629648763766;
            double last = (third - fourth) * 0.7071067811865476;
            value0 = first + second;
            value1 = third + fourth + last;
            value2 = (first - second) * 0.7071067811865476;
            value3 = last;
        }

        private static double[] CreateFactors()
        {
            double[] result = new double[32];
            for (int size = 4; size <= 32; size *= 2)
            {
                for (int index = 0; index < size / 2; index++)
                {
                    result[size / 2 + index] = 1 / (2 * Math.Cos(Math.PI * (index + 0.5) / size));
                }
            }
            return result;
        }
    }
}
