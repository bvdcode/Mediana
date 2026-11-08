// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Ac3
{
    internal static class Ac3Synthesis
    {
        private static readonly Complex[] LongRotations = CreateRotations(128);
        private static readonly Complex[] ShortRotations = CreateRotations(64);
        private static readonly Complex[] FourierRotations = CreateFourierRotations();
        private static readonly double[] Window = CreateWindow();

        public static void Process(ReadOnlySpan<double> spectrum, Span<double> delay, Span<float> pcm,
            int stride, bool shortBlock, double gain)
        {
            Span<Complex> first = stackalloc Complex[128];
            Span<Complex> second = stackalloc Complex[64];
            Span<double> samples = stackalloc double[512];
            if (shortBlock)
            {
                first = first[..64];
                Transform(spectrum, first, ShortRotations, 2, 0);
                Transform(spectrum, second, ShortRotations, 2, 1);
                for (int n = 0; n < 64; n++)
                {
                    samples[2 * n] = -first[n].Imaginary * Window[2 * n];
                    samples[2 * n + 1] = first[63 - n].Real * Window[2 * n + 1];
                    samples[128 + 2 * n] = -first[n].Real * Window[128 + 2 * n];
                    samples[129 + 2 * n] = first[63 - n].Imaginary * Window[129 + 2 * n];
                    samples[256 + 2 * n] = -second[n].Real * Window[255 - 2 * n];
                    samples[257 + 2 * n] = second[63 - n].Imaginary * Window[254 - 2 * n];
                    samples[384 + 2 * n] = second[n].Imaginary * Window[127 - 2 * n];
                    samples[385 + 2 * n] = -second[63 - n].Real * Window[126 - 2 * n];
                }
            }
            else
            {
                Transform(spectrum, first, LongRotations, 1, 0);
                for (int n = 0; n < 64; n++)
                {
                    samples[2 * n] = -first[64 + n].Imaginary * Window[2 * n];
                    samples[2 * n + 1] = first[63 - n].Real * Window[2 * n + 1];
                    samples[128 + 2 * n] = -first[n].Real * Window[128 + 2 * n];
                    samples[129 + 2 * n] = first[127 - n].Imaginary * Window[129 + 2 * n];
                    samples[256 + 2 * n] = -first[64 + n].Real * Window[255 - 2 * n];
                    samples[257 + 2 * n] = first[63 - n].Imaginary * Window[254 - 2 * n];
                    samples[384 + 2 * n] = first[n].Imaginary * Window[127 - 2 * n];
                    samples[385 + 2 * n] = -first[127 - n].Real * Window[126 - 2 * n];
                }
            }
            for (int n = 0; n < 256; n++)
            {
                pcm[n * stride] = (float)Math.Clamp(2 * (samples[n] * gain + delay[n]), -1, 1);
                delay[n] = samples[n + 256] * gain;
            }
        }

        private static void Transform(ReadOnlySpan<double> spectrum, Span<Complex> values,
            ReadOnlySpan<Complex> rotations, int stride, int offset)
        {
            int count = values.Length;
            for (int k = 0; k < count; k++)
            {
                values[k] = new Complex(spectrum[(2 * count - 2 * k - 1) * stride + offset],
                    spectrum[2 * k * stride + offset]) * rotations[k];
            }
            for (int k = 1, reversed = 0; k < count; k++)
            {
                int bit = count >> 1;
                for (; (reversed & bit) != 0; bit >>= 1) { reversed ^= bit; }
                reversed ^= bit;
                if (k < reversed) { (values[k], values[reversed]) = (values[reversed], values[k]); }
            }
            for (int width = 2; width <= count; width *= 2)
            {
                int half = width / 2, step = 128 / width;
                for (int start = 0; start < count; start += width)
                {
                    for (int k = 0; k < half; k++)
                    {
                        Complex odd = values[start + k + half] * FourierRotations[k * step];
                        Complex even = values[start + k];
                        values[start + k] = even + odd; values[start + k + half] = even - odd;
                    }
                }
            }
            for (int n = 0; n < count; n++) { values[n] *= rotations[n]; }
        }

        private static Complex[] CreateFourierRotations()
        {
            Complex[] result = new Complex[64];
            for (int k = 0; k < result.Length; k++)
            {
                (double sine, double cosine) = Math.SinCos(2 * Math.PI * k / 128);
                result[k] = new(cosine, sine);
            }
            return result;
        }

        private static Complex[] CreateRotations(int count)
        {
            Complex[] result = new Complex[count];
            for (int k = 0; k < count; k++)
            {
                (double sine, double cosine) = Math.SinCos(Math.PI * (k + 0.125) / (2 * count));
                result[k] = new(-cosine, -sine);
            }
            return result;
        }

        private static double[] CreateWindow()
        {
            double[] values = new double[256];
            Span<double> kaiser = stackalloc double[257];
            double total = 0;
            for (int k = 0; k <= 256; k++)
            {
                double ratio = (k - 128) / 128.0;
                double x = 5 * Math.PI * Math.Sqrt(Math.Max(0, 1 - ratio * ratio));
                double sum = 1, term = 1;
                for (int n = 1; n < 40; n++) { term *= x * x / (4 * n * n); sum += term; }
                total += sum; kaiser[k] = sum;
            }
            double cumulative = 0;
            for (int k = 0; k < 256; k++) { cumulative += kaiser[k]; values[k] = Math.Sqrt(cumulative / total); }
            return values;
        }
    }
}
