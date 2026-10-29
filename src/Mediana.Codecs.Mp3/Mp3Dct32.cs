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
            Forward(values[..32], scratch);
        }

        private static void Forward(Span<double> values, Span<double> scratch)
        {
            if (values.Length == 4)
            {
                ForwardFour(values);
                return;
            }
            int half = values.Length / 2;
            for (int index = 0; index < half; index++)
            {
                double first = values[index];
                double last = values[values.Length - index - 1];
                scratch[index] = first + last;
                scratch[half + index] = (first - last) * Factors[half + index];
            }
            Forward(scratch[..half], values[..half]);
            Forward(scratch.Slice(half, half), values.Slice(half, half));
            for (int index = 0; index < half - 1; index++)
            {
                values[2 * index] = scratch[index];
                values[2 * index + 1] = scratch[half + index] + scratch[half + index + 1];
            }
            values[^2] = scratch[half - 1];
            values[^1] = scratch[2 * half - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ForwardFour(Span<double> values)
        {
            double first = values[0] + values[3];
            double second = values[1] + values[2];
            double third = (values[0] - values[3]) * 0.541196100146197;
            double fourth = (values[1] - values[2]) * 1.3065629648763766;
            double last = (third - fourth) * 0.7071067811865476;
            values[0] = first + second;
            values[1] = third + fourth + last;
            values[2] = (first - second) * 0.7071067811865476;
            values[3] = last;
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
