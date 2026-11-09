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
            fourier = new AacFourierPlan(count);
            double phase = Math.PI / size;
            for (int index = 0; index < count; index++)
            {
                (double sine, double cosine) = Math.SinCos(-phase * index);
                PreRotation[index] = new Complex(cosine, sine);
                (sine, cosine) = Math.SinCos(-phase * (index + 0.25));
                PostRotation[index] = new Complex(cosine / size, sine / size);
            }
        }

        public Complex[] PreRotation { get; }
        public Complex[] PostRotation { get; }
        private readonly AacFourierPlan fourier;
        public int[] Reversed => fourier.Reversed;
        public Complex[] Rotations => fourier.Rotations;
    }
}
