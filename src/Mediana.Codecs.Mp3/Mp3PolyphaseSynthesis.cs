// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3PolyphaseSynthesis
    {
        private static readonly double[] Matrix = CreateMatrix();

        public static void Process(ReadOnlySpan<double> subbands, Span<double> history, ref int position,
            Span<float> output, int channels, int channel)
        {
            Span<double> samples = stackalloc double[32];
            ReadOnlySpan<int> window = Mp3SynthesisWindow.Coefficients;
            for (int time = 0; time < 18; time++)
            {
                for (int subband = 0; subband < 32; subband++)
                {
                    samples[subband] = subbands[subband * 18 + time];
                }
                position = (position - 64) & 1023;
                for (int index = 0; index < 64; index++)
                {
                    history[position + index] = Mp3TransformMath.Dot(samples, Matrix.AsSpan(index * 32, 32));
                }
                for (int sample = 0; sample < 32; sample++)
                {
                    double value = 0;
                    for (int block = 0; block < 8; block++)
                    {
                        value += history[(position + block * 128 + sample) & 1023] * window[block * 64 + sample];
                        value += history[(position + block * 128 + sample + 96) & 1023] * window[block * 64 + sample + 32];
                    }
                    float pcm = (float)(value / 65536);
                    if (!float.IsFinite(pcm))
                    {
                        throw new InvalidDataException("MP3 synthesis produced a nonfinite PCM sample.");
                    }
                    output[(time * 32 + sample) * channels + channel] = pcm;
                }
            }
        }

        private static double[] CreateMatrix()
        {
            double[] matrix = new double[64 * 32];
            for (int sample = 0; sample < 64; sample++)
            {
                for (int subband = 0; subband < 32; subband++)
                {
                    matrix[sample * 32 + subband] = Math.Cos(Math.PI / 64 * (16 + sample) * (2 * subband + 1));
                }
            }
            return matrix;
        }
    }
}
