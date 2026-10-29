// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3HybridSynthesis
    {
        private static readonly double[][] LongMatrices = [CreateMatrix(0), CreateMatrix(1), [], CreateMatrix(3)];
        private static readonly double[] ShortMatrix = CreateMatrix(2);
        private static readonly double[] AliasCosines = CreateAlias(false);
        private static readonly double[] AliasSines = CreateAlias(true);

        public static void Process(Span<double> spectrum, Mp3GranuleInfo info, int sampleRate,
            Span<double> overlap, Span<double> output)
        {
            int longSubbands = info.BlockType == 2 ? (info.MixedBlock ? (sampleRate == 8000 ? 4 : 2) : 0) : 32;
            for (int subband = 1; subband < longSubbands; subband++)
            {
                for (int index = 0; index < 8; index++)
                {
                    int lower = subband * 18 - 1 - index;
                    int upper = subband * 18 + index;
                    double a = spectrum[lower];
                    double b = spectrum[upper];
                    spectrum[lower] = a * AliasCosines[index] - b * AliasSines[index];
                    spectrum[upper] = b * AliasCosines[index] + a * AliasSines[index];
                }
            }
            Span<double> time = stackalloc double[36];
            for (int subband = 0; subband < 32; subband++)
            {
                ReadOnlySpan<double> input = spectrum.Slice(subband * 18, 18);
                bool shortBlock = info.BlockType == 2 && subband >= longSubbands;
                if (shortBlock)
                {
                    time.Clear();
                    for (int window = 0; window < 3; window++)
                    {
                        ReadOnlySpan<double> part = input.Slice(window * 6, 6);
                        for (int sample = 0; sample < 12; sample++)
                        {
                            time[6 + window * 6 + sample] += Mp3TransformMath.Dot(part, ShortMatrix.AsSpan(sample * 6, 6));
                        }
                    }
                }
                else
                {
                    int shape = info.BlockType == 2 ? 0 : info.BlockType;
                    ReadOnlySpan<double> matrix = LongMatrices[shape];
                    for (int sample = 0; sample < 36; sample++)
                    {
                        time[sample] = Mp3TransformMath.Dot(input, matrix.Slice(sample * 18, 18));
                    }
                }
                for (int sample = 0; sample < 18; sample++)
                {
                    int index = subband * 18 + sample;
                    double value = time[sample] + overlap[index];
                    overlap[index] = time[sample + 18];
                    output[index] = (subband & sample & 1) != 0 ? -value : value;
                }
            }
        }

        private static double[] CreateMatrix(int shape)
        {
            int count = shape == 2 ? 6 : 18;
            double[] matrix = new double[2 * count * count];
            for (int sample = 0; sample < 2 * count; sample++)
            {
                double window = shape switch
                {
                    0 or 2 => Math.Sin(Math.PI / (2 * count) * (sample + 0.5)),
                    1 when sample < 18 => Math.Sin(Math.PI / 36 * (sample + 0.5)),
                    1 when sample < 24 => 1,
                    1 when sample < 30 => Math.Sin(Math.PI / 12 * (sample - 18 + 0.5)),
                    1 => 0,
                    3 when sample < 6 => 0,
                    3 when sample < 12 => Math.Sin(Math.PI / 12 * (sample - 6 + 0.5)),
                    3 when sample < 18 => 1,
                    3 => Math.Sin(Math.PI / 36 * (sample + 0.5)),
                    _ => throw new InvalidOperationException("The MP3 synthesis window is invalid.")
                };
                for (int frequency = 0; frequency < count; frequency++)
                {
                    matrix[sample * count + frequency] = window * Math.Cos(Math.PI / count
                        * (sample + 0.5 + count / 2.0) * (frequency + 0.5));
                }
            }
            return matrix;
        }

        private static double[] CreateAlias(bool sine)
        {
            double[] values = [-0.6, -0.535, -0.33, -0.185, -0.095, -0.041, -0.0142, -0.0037];
            for (int index = 0; index < values.Length; index++)
            {
                double value = values[index];
                values[index] = (sine ? value : 1) / Math.Sqrt(1 + value * value);
            }
            return values;
        }
    }
}
