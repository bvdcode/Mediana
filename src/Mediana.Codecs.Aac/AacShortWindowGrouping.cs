// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal static class AacShortWindowGrouping
    {
        private const double MaximumEnergyRatio = 4;

        public static int Create(AacEncoderWorkspace workspace, int shape, int previousShape, Span<int> groups)
        {
            if (groups.Length < 8)
            {
                throw new ArgumentException("AAC grouping requires storage for eight window groups.", nameof(groups));
            }
            int channels = workspace.Channels;
            Span<double> energies = stackalloc double[channels * 8];
            ReadOnlySpan<double> right = AacWindows.Get(128, shape);
            for (int channel = 0; channel < channels; channel++)
            {
                ReadOnlySpan<double> input = workspace.Input(channel);
                ReadOnlySpan<double> left = AacWindows.Get(128, previousShape);
                for (int window = 0; window < 8; window++)
                {
                    int start = 448 + window * 128;
                    double energy = 0;
                    for (int sample = 0; sample < 128; sample++)
                    {
                        double first = input[start + sample] * left[sample];
                        double second = input[start + 128 + sample] * right[127 - sample];
                        energy += first * first + second * second;
                    }
                    energies[channel * 8 + window] = energy;
                    left = right;
                }
            }
            Span<double> minima = stackalloc double[channels];
            Span<double> maxima = stackalloc double[channels];
            for (int channel = 0; channel < channels; channel++)
            {
                minima[channel] = maxima[channel] = energies[channel * 8];
            }
            int count = 1;
            groups[0] = 1;
            for (int window = 1; window < 8; window++)
            {
                bool split = false;
                for (int channel = 0; channel < channels; channel++)
                {
                    double energy = energies[channel * 8 + window];
                    double minimum = Math.Min(minima[channel], energy);
                    double maximum = Math.Max(maxima[channel], energy);
                    split |= maximum > minimum * MaximumEnergyRatio;
                }
                if (split)
                {
                    groups[count++] = 1;
                }
                else
                {
                    groups[count - 1]++;
                }
                for (int channel = 0; channel < channels; channel++)
                {
                    double energy = energies[channel * 8 + window];
                    minima[channel] = split ? energy : Math.Min(minima[channel], energy);
                    maxima[channel] = split ? energy : Math.Max(maxima[channel], energy);
                }
            }
            return count;
        }
    }
}
