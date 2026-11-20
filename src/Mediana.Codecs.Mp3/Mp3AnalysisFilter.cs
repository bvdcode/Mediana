// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3AnalysisFilter
    {
        public const int DelaySamples = 481;
        private static readonly double[] Matrix = CreateMatrix();

        public static void Process(ReadOnlySpan<float> input, int channels, int channel, Span<double> history,
            Span<double> previous, Span<double> spectrum)
        {
            Span<double> subbands = stackalloc double[576];
            Span<double> summed = stackalloc double[64];
            ReadOnlySpan<int> window = Mp3SynthesisWindow.Coefficients;
            for (int time = 0; time < 18; time++)
            {
                history[..480].CopyTo(history[32..]);
                for (int index = 0; index < 32; index++)
                {
                    history[31 - index] = input[(time * 32 + index) * channels + channel];
                }
                for (int index = 0; index < 64; index++)
                {
                    double value = 0;
                    for (int block = 0; block < 8; block++)
                    {
                        int position = block * 64 + index;
                        value += history[position] * window[position];
                    }
                    summed[index] = value / 2097152;
                }
                for (int band = 0; band < 32; band++)
                {
                    double value = Mp3TransformMath.Dot(summed, Matrix.AsSpan(band * 64, 64));
                    subbands[band * 18 + time] = (band & time & 1) != 0 ? -value : value;
                }
            }
            Mp3HybridAnalysis.Process(previous, subbands, spectrum);
            subbands.CopyTo(previous);
        }

        private static double[] CreateMatrix()
        {
            double[] result = new double[32 * 64];
            for (int band = 0; band < 32; band++)
            {
                for (int sample = 0; sample < 64; sample++)
                {
                    result[band * 64 + sample] = Math.Cos((2 * band + 1) * (sample - 16) * Math.PI / 64);
                }
            }
            return result;
        }
    }
}
