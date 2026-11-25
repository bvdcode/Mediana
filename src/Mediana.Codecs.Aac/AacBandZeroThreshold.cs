// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal readonly record struct AacBandZeroThreshold(int LastNonzeroScale, double ZeroError)
    {
        public int CoarsestNonzeroScale(int maximumScale) => Math.Min(maximumScale, LastNonzeroScale);

        public static AacBandZeroThreshold Measure(ReadOnlySpan<double> spectrum, double normalization)
        {
            double maximum = 0;
            double energy = 0;
            foreach (double value in spectrum)
            {
                maximum = Math.Max(maximum, Math.Abs(value));
                energy += value * value;
            }
            return new(AacQuantizer.CoarsestNonzeroScale(maximum), normalization == 0 ? energy : energy / normalization);
        }
    }
}
