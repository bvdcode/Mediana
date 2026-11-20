// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3HybridAnalysis
    {
        public const int DelaySamples = 576;
        private static readonly double[] Matrix = CreateMatrix();

        public static void Process(ReadOnlySpan<double> previous, ReadOnlySpan<double> current, Span<double> spectrum)
        {
            Span<double> input = stackalloc double[36];
            for (int band = 0; band < 32; band++)
            {
                previous.Slice(band * 18, 18).CopyTo(input);
                current.Slice(band * 18, 18).CopyTo(input[18..]);
                for (int frequency = 0; frequency < 18; frequency++)
                {
                    spectrum[band * 18 + frequency] = Mp3TransformMath.Dot(input, Matrix.AsSpan(frequency * 36, 36));
                }
            }
            ReadOnlySpan<double> cosines = Mp3HybridSynthesis.AliasCosines;
            ReadOnlySpan<double> sines = Mp3HybridSynthesis.AliasSines;
            for (int band = 31; band > 0; band--)
            {
                for (int index = 0; index < 8; index++)
                {
                    int lower = band * 18 - 1 - index;
                    int upper = band * 18 + index;
                    double a = spectrum[lower];
                    double b = spectrum[upper];
                    spectrum[lower] = a * cosines[index] + b * sines[index];
                    spectrum[upper] = b * cosines[index] - a * sines[index];
                }
            }
        }

        private static double[] CreateMatrix()
        {
            double[] result = new double[18 * 36];
            for (int frequency = 0; frequency < 18; frequency++)
            {
                for (int sample = 0; sample < 36; sample++)
                {
                    result[frequency * 36 + sample] = Math.Cos(Math.PI / 18 * (sample + 9.5) * (frequency + 0.5))
                        * Math.Sin(Math.PI / 36 * (sample + 0.5)) / 9;
                }
            }
            return result;
        }
    }
}
