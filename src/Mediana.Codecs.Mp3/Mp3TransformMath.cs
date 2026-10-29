// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3TransformMath
    {
        public static double Dot(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
        {
            Vector<double> sum = Vector<double>.Zero;
            int index = 0;
            for (; index <= left.Length - Vector<double>.Count; index += Vector<double>.Count)
            {
                sum += new Vector<double>(left[index..]) * new Vector<double>(right[index..]);
            }
            double result = Vector.Sum(sum);
            for (; index < left.Length; index++)
            {
                result += left[index] * right[index];
            }
            return result;
        }
    }
}
