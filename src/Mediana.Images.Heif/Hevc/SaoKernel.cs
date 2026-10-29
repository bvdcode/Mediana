// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal static class SaoKernel
    {
        public static int Band(int sample, int depth, SaoParameters parameters)
        {
            int category = ((sample >> (depth - 5)) - parameters.Band) & 31;
            return category >= 4 ? sample : CoefficientMath.ClipSample(sample + parameters.Offset(category + 1), depth);
        }

        public static int Edge(int sample, int first, int second, int depth, SaoParameters parameters)
        {
            int category = 2 + Math.Sign(sample - first) + Math.Sign(sample - second);
            if (category <= 2)
            {
                category = category == 2 ? 0 : category + 1;
            }
            return CoefficientMath.ClipSample(sample + parameters.Offset(category), depth);
        }
    }
}
