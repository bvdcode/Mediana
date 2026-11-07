// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class ReconstructionKernel
    {
        public static int Taps(ChromaUpsamplingFilter filter) => filter switch
        {
            ChromaUpsamplingFilter.NearestNeighbor => 1,
            ChromaUpsamplingFilter.Bilinear => 2,
            ChromaUpsamplingFilter.Bicubic => 4,
            ChromaUpsamplingFilter.Lanczos3 => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        public static double Weight(ChromaUpsamplingFilter filter, double distance) => filter switch
        {
            ChromaUpsamplingFilter.NearestNeighbor => 1,
            ChromaUpsamplingFilter.Bilinear => Math.Max(0, 1 - Math.Abs(distance)),
            ChromaUpsamplingFilter.Bicubic => Cubic(Math.Abs(distance)),
            ChromaUpsamplingFilter.Lanczos3 => Lanczos(Math.Abs(distance)),
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        private static double Cubic(double x)
        {
            if (x < 1)
            {
                return ((1.5 * x - 2.5) * x * x) + 1;
            }
            if (x < 2)
            {
                return (((-0.5 * x + 2.5) * x - 4) * x) + 2;
            }
            return 0;
        }

        private static double Lanczos(double x)
        {
            if (x == 0)
            {
                return 1;
            }
            if (x >= 3)
            {
                return 0;
            }
            double angle = Math.PI * x;
            return Math.Sin(angle) * Math.Sin(angle / 3) / (angle * angle / 3);
        }
    }
}
