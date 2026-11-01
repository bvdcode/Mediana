// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegSequentialBlock
    {
        internal static void Decode(ref JpegEntropyReader bits, JpegHuffmanTable dc, JpegHuffmanTable ac,
            ref int predictor, scoped Span<int> coefficients)
        {
            int category = dc.Decode(ref bits);
            if (category > 11)
            {
                throw new InvalidDataException("JPEG 8-bit DC difference category exceeds eleven bits.");
            }
            predictor += bits.Receive(category);
            if (predictor is < -2048 or > 2047)
            {
                throw new InvalidDataException("JPEG DC predictor exceeds the supported coefficient range.");
            }
            coefficients[0] = predictor;
            int position = 1;
            while (position < 64)
            {
                int symbol = ac.Decode(ref bits);
                int run = symbol >> 4;
                int size = symbol & 15;
                if (size == 0)
                {
                    if (run == 0)
                    {
                        return;
                    }
                    if (run != 15 || position + 16 > 64)
                    {
                        throw new InvalidDataException("JPEG AC zero run is invalid or exceeds the coefficient block.");
                    }
                    position += 16;
                }
                else
                {
                    position += run;
                    if (size > 10 || position >= 64)
                    {
                        throw new InvalidDataException("JPEG AC coefficient size or zero run is invalid.");
                    }
                    coefficients[JpegInverseTransform.Zigzag[position++]] = bits.Receive(size);
                }
            }
        }
    }
}
