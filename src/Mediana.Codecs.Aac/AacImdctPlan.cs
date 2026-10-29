// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal class AacImdctPlan
    {
        public AacImdctPlan(int size)
        {
            int count = size / 2;
            PreRotation = new Complex[count];
            PostRotation = new Complex[count];
            Reversed = new int[count];
            Rotations = new Complex[count - 1];
            double phase = Math.PI / size;
            for (int index = 0; index < count; index++)
            {
                (double sine, double cosine) = Math.SinCos(-phase * index);
                PreRotation[index] = new Complex(cosine, sine);
                (sine, cosine) = Math.SinCos(-phase * (index + 0.25));
                PostRotation[index] = new Complex(cosine / size, sine / size);
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

        public Complex[] PreRotation { get; }
        public Complex[] PostRotation { get; }
        public int[] Reversed { get; }
        public Complex[] Rotations { get; }
    }
}
