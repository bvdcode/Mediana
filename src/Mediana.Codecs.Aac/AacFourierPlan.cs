// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal class AacFourierPlan
    {
        public AacFourierPlan(int count)
        {
            if (count < 4 || (count & (count - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            Reversed = new int[count];
            Rotations = new Complex[count - 1];
            for (int index = 0; index < count; index++)
            {
                int reversed = 0;
                for (int value = index, bit = count / 2; bit != 0; bit >>= 1, value >>= 1)
                {
                    reversed |= (value & 1) * bit;
                }
                Reversed[index] = reversed;
            }
            int width = 4;
            for (; width <= count; width *= 4)
            {
                int quarter = width / 4;
                for (int offset = 0; offset < quarter; offset++)
                {
                    for (int factor = 1; factor <= 3; factor++)
                    {
                        (double sine, double cosine) = Math.SinCos(-2 * Math.PI * factor * offset / width);
                        Rotations[quarter - 1 + (factor - 1) * quarter + offset] = new Complex(cosine, sine);
                    }
                }
            }
            if (width / 4 != count)
            {
                for (int offset = 0; offset < count / 2; offset++)
                {
                    (double sine, double cosine) = Math.SinCos(-2 * Math.PI * offset / count);
                    Rotations[count / 2 - 1 + offset] = new Complex(cosine, sine);
                }
            }
        }

        public int[] Reversed { get; }
        public Complex[] Rotations { get; }
    }
}
