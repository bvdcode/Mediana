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
            (byte book, int bits) = AacHuffmanEncoder.SelectCodebook(quantized);
            return new(scale, book, bits, error);
        }
    }
}
