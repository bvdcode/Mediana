// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal readonly record struct AacBandQuantization(int Scale, byte Book, int Bits, double Error)
    {
        public static AacBandQuantization Measure(ReadOnlySpan<double> spectrum, ReadOnlySpan<double> powered, Span<int> quantized, int scale)
        {
            double error = AacQuantizer.Quantize(spectrum, powered, quantized, scale);
            if (!double.IsFinite(error))
            {
                return new(scale, 0, 0, double.PositiveInfinity);
            }
            int maximum = 0;
            foreach (int value in quantized)
            {
                maximum = Math.Max(maximum, Math.Abs(value));
            }
            if (maximum == 0)
            {
                return new(scale, 0, 0, error);
            }
            byte book = 0;
            int best = int.MaxValue;
            for (int codebook = 1; codebook <= 11; codebook++)
            {
                int bits = AacHuffmanEncoder.Count(codebook, quantized);
                if (bits < best)
                {
                    best = bits;
                    book = (byte)codebook;
                }
            }
            return new(scale, book, best, error);
        }
    }
}
