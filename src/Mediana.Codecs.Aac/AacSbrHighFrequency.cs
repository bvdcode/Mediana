// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacSbrHighFrequency
    {
        public static void Generate(ReadOnlySpan<Complex> low, Span<Complex> high, AacSbrChannelData data,
            AacSbrFrequencyTables tables, AacSbrPatches patches, Span<double> chirp, Span<int> previousFiltering)
        {
            for (int band = 0; band < tables.NoiseCount; band++)
            {
                int mode = data.InverseFiltering[band];
                double next = mode switch
                {
                    0 => previousFiltering[band] == 1 ? 0.6 : 0,
                    1 => previousFiltering[band] == 0 ? 0.6 : 0.75,
                    2 => 0.9,
                    3 => 0.98,
                    _ => throw new InvalidDataException("The SBR inverse filtering mode is invalid.")
                };
                double weight = next < chirp[band] ? 0.75 : 0.90625;
                next = next * weight + chirp[band] * (1 - weight);
                chirp[band] = next < 0.015625 ? 0 : Math.Min(next, 0.99609375);
                previousFiltering[band] = mode;
            }
            Span<Complex> first = stackalloc Complex[32];
            Span<Complex> second = stackalloc Complex[32];
            for (int source = 0; source < tables.StartBand; source++)
            {
                Predict(low, source, out first[source], out second[source]);
            }
            int noiseBand = 0;
            int start = data.Grid.Borders[0] * 2 + 2;
            int end = data.Grid.Borders[^1] * 2 + 2;
            for (int band = tables.CrossoverBand; band < tables.StopBand; band++)
            {
                while (band >= tables.Noise[noiseBand + 1])
                {
                    noiseBand++;
                }
                int source = patches.SourceBands[band];
                Complex a = source < 0 ? Complex.Zero : first[source] * chirp[noiseBand];
                Complex b = source < 0 ? Complex.Zero : second[source] * (chirp[noiseBand] * chirp[noiseBand]);
                for (int slot = start; slot < end; slot++)
                {
                    high[slot * 64 + band] = source < 0 ? Complex.Zero : low[slot * 32 + source] +
                        a * low[(slot - 1) * 32 + source] + b * low[(slot - 2) * 32 + source];
                }
            }
        }

        private static void Predict(ReadOnlySpan<Complex> samples, int band, out Complex first, out Complex second)
        {
            Complex r01 = Complex.Zero;
            Complex r02 = Complex.Zero;
            Complex r12 = Complex.Zero;
            double r11 = 0;
            double r22 = 0;
            for (int slot = 2; slot < 40; slot++)
            {
                Complex current = samples[slot * 32 + band];
                Complex previous = samples[(slot - 1) * 32 + band];
                Complex older = samples[(slot - 2) * 32 + band];
                r01 += current * Complex.Conjugate(previous);
                r02 += current * Complex.Conjugate(older);
                r12 += previous * Complex.Conjugate(older);
                r11 += previous.Real * previous.Real + previous.Imaginary * previous.Imaginary;
                r22 += older.Real * older.Real + older.Imaginary * older.Imaginary;
            }
            double determinant = r11 * r22 - (r12.Real * r12.Real + r12.Imaginary * r12.Imaginary) / 1.000001;
            second = determinant == 0 ? Complex.Zero : (r01 * r12 - r02 * r11) / determinant;
            first = r11 == 0 ? Complex.Zero : -(r01 + second * Complex.Conjugate(r12)) / r11;
            if (first.Real * first.Real + first.Imaginary * first.Imaginary >= 16 ||
                second.Real * second.Real + second.Imaginary * second.Imaginary >= 16)
            {
                first = second = Complex.Zero;
            }
        }
    }
}
