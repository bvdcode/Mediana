// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3Requantization
    {
        private static readonly double[] Magnitudes = CreateMagnitudes();
        private static ReadOnlySpan<byte> Pretab => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 3, 2, 0];
        private static ReadOnlySpan<double> QuarterPowers => [1, 1.189207115002721, 1.4142135623730951, 1.681792830507429];

        public static void Apply(ReadOnlySpan<int> input, ReadOnlySpan<byte> scales, Mp3GranuleInfo info,
            ReadOnlySpan<Mp3SpectralBand> bands, Span<double> output)
        {
            int multiplier = info.ScaleFactorScale ? 4 : 2;
            for (int index = 0; index < bands.Length; index++)
            {
                Mp3SpectralBand band = bands[index];
                int exponent = info.GlobalGain - 210 - multiplier * scales[index];
                if (band.Window < 0)
                {
                    if (info.Preflag)
                    {
                        exponent -= multiplier * Pretab[band.Band];
                    }
                }
                else
                {
                    int gain = band.Window switch
                    {
                        0 => info.SubblockGain0,
                        1 => info.SubblockGain1,
                        2 => info.SubblockGain2,
                        _ => throw new InvalidOperationException("The MP3 short window index is invalid.")
                    };
                    exponent -= 8 * gain;
                }
                double factor = Math.ScaleB(QuarterPowers[exponent & 3], exponent >> 2);
                for (int offset = band.Offset; offset < band.Offset + band.Width; offset++)
                {
                    int value = input[offset];
                    if (value < -8206 || value > 8206)
                    {
                        throw new InvalidDataException("The MP3 quantized magnitude exceeds its coded range.");
                    }
                    output[offset] = value < 0 ? -Magnitudes[-value] * factor : Magnitudes[value] * factor;
                }
            }
        }

        private static double[] CreateMagnitudes()
        {
            double[] result = new double[8207];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Math.Pow(index, 4.0 / 3.0);
            }
            return result;
        }
    }
}
