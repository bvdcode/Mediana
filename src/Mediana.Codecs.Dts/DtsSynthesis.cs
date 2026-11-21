// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Transforms;

namespace Mediana.Codecs.Dts
{
    internal static class DtsSynthesis
    {
        private static readonly double[] Factors = CreateFactors();
        private static readonly double PcmScale = 1 / (65536 * Math.Sqrt(2));

        public static void Apply(ReadOnlySpan<double> bands, Span<double> history, bool perfect,
            Span<float> output, int channel, int channels, double gain)
        {
            Span<double> x = history[..512], z = history[512..];
            for (int i = 0; i < 32; i++) { x[i] = bands[i] * Factors[i]; }
            Dct32.Transform(x[..32]);
            for (int i = 0; i < 31; i++) { x[i] += x[i + 1]; }
            ReadOnlySpan<double> filter = perfect ? DtsFilterTables.Perfect : DtsFilterTables.NonPerfect;
            for (int i = 0; i < 32; i++)
            {
                double first = z[i], second = z[i + 32];
                for (int j = 0; j < 512; j += 64)
                {
                    first += filter[i + j] * (x[i + j] - x[j + 31 - i]);
                    second += filter[i + j + 32] * (-x[i + j] - x[j + 31 - i]);
                }
                output[i * channels + channel] = (float)(first * PcmScale * gain);
                z[i] = second;
                z[i + 32] = 0;
            }
            x[..480].CopyTo(x[32..]);
        }

        public static void LowFrequency(ReadOnlySpan<double> samples, Span<double> history, int decimation,
            Span<float> output, int channel, int channels, double gain)
        {
            ReadOnlySpan<double> filter = decimation == 64 ? DtsFilterTables.Lfe64 : DtsFilterTables.Lfe128;
            int taps = 512 / decimation;
            int sampleIndex = 0;
            foreach (double sample in samples)
            {
                history[..(taps - 1)].CopyTo(history[1..]);
                history[0] = sample;
                for (int phase = 0; phase < decimation; phase++)
                {
                    double sum = 0;
                    for (int tap = 0; tap < taps; tap++) { sum += history[tap] * filter[phase + tap * decimation]; }
                    int position = (sampleIndex * decimation + phase) * channels + channel;
                    if (position < output.Length) { output[position] = (float)(sum * gain / 8388608); }
                }
                sampleIndex++;
            }
        }

        private static double[] CreateFactors()
        {
            double[] result = new double[32];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = 0.25 / Math.Cos(Math.PI * (i + 0.5) / 64);
            }
            return result;
        }
    }
}
