// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegProgressiveBlock
    {
        internal static void Decode(ref JpegEntropyReader bits, JpegHuffmanTable? dc, JpegHuffmanTable? ac,
            JpegScanParameters scan, ref int predictor, ref int endOfBands, int remaining, scoped Span<int> coefficients)
        {
            int step = 1 << scan.Low;
            if (scan.First == 0)
            {
                if (scan.High == 0)
                {
                    int size = dc!.Decode(ref bits);
                    if (size > 11)
                    {
                        throw new InvalidDataException("JPEG 8-bit DC difference category exceeds eleven bits.");
                    }
                    predictor += bits.Receive(size);
                    int value = predictor * step;
                    if (value is < -2048 or > 2047)
                    {
                        throw new InvalidDataException("Progressive JPEG DC predictor exceeds the supported coefficient range.");
                    }
                    coefficients[0] = value;
                }
                else
                {
                    coefficients[0] |= bits.ReadBits(1) * step;
                    if (coefficients[0] is < -2048 or > 2047)
                    {
                        throw new InvalidDataException("Progressive JPEG DC refinement exceeds the supported coefficient range.");
                    }
                }
                return;
            }
            if (scan.High == 0)
            {
                ReadInitial(ref bits, ac!, scan, step, ref endOfBands, remaining, coefficients);
            }
            else
            {
                ReadRefinement(ref bits, ac!, scan, step, ref endOfBands, remaining, coefficients);
            }
        }

        private static void ReadInitial(ref JpegEntropyReader bits, JpegHuffmanTable table, JpegScanParameters scan,
            int step, ref int endOfBands, int remaining, scoped Span<int> coefficients)
        {
            if (endOfBands != 0)
            {
                endOfBands--;
                return;
            }
            int position = scan.First;
            while (position <= scan.Last)
            {
                int symbol = table.Decode(ref bits);
                int run = symbol >> 4;
                int size = symbol & 15;
                if (size == 0)
                {
                    if (run != 15)
                    {
                        endOfBands = ReadEndOfBands(ref bits, run, remaining) - 1;
                        return;
                    }
                    position += 16;
                    if (position > scan.Last + 1)
                    {
                        throw new InvalidDataException("Progressive JPEG zero run exceeds the spectral band.");
                    }
                    continue;
                }
                position += run;
                if (size > 10 || position > scan.Last)
                {
                    throw new InvalidDataException("Progressive JPEG AC coefficient size or zero run is invalid.");
                }
                int value = bits.Receive(size) * step;
                if (value is < -1023 or > 1023)
                {
                    throw new InvalidDataException("Progressive JPEG AC coefficient exceeds ten bits.");
                }
                coefficients[JpegInverseTransform.Zigzag[position++]] = value;
            }
        }

        private static void ReadRefinement(ref JpegEntropyReader bits, JpegHuffmanTable table, JpegScanParameters scan,
            int step, ref int endOfBands, int remaining, scoped Span<int> coefficients)
        {
            int position = scan.First;
            while (endOfBands == 0 && position <= scan.Last)
            {
                int symbol = table.Decode(ref bits);
                int run = symbol >> 4;
                int size = symbol & 15;
                int value = 0;
                if (size == 0)
                {
                    if (run != 15)
                    {
                        endOfBands = ReadEndOfBands(ref bits, run, remaining);
                        break;
                    }
                    run = 16;
                }
                else if (size == 1)
                {
                    if (step > 1023)
                    {
                        throw new InvalidDataException("Progressive JPEG AC coefficient exceeds ten bits.");
                    }
                    value = bits.ReadBits(1) == 0 ? -step : step;
                }
                else
                {
                    throw new InvalidDataException("Progressive JPEG AC refinement requires one-bit new coefficients.");
                }
                while (position <= scan.Last)
                {
                    ref int coefficient = ref coefficients[JpegInverseTransform.Zigzag[position]];
                    if (coefficient != 0)
                    {
                        Refine(ref bits, ref coefficient, step);
                    }
                    else
                    {
                        if (run == 0)
                        {
                            break;
                        }
                        run--;
                    }
                    position++;
                    if (value == 0 && run == 0)
                    {
                        break;
                    }
                }
                if (run != 0 || (value != 0 && position > scan.Last))
                {
                    throw new InvalidDataException("Progressive JPEG refinement zero run exceeds the spectral band.");
                }
                if (value != 0)
                {
                    coefficients[JpegInverseTransform.Zigzag[position++]] = value;
                }
            }
            if (endOfBands != 0)
            {
                while (position <= scan.Last)
                {
                    ref int coefficient = ref coefficients[JpegInverseTransform.Zigzag[position++]];
                    if (coefficient != 0)
                    {
                        Refine(ref bits, ref coefficient, step);
                    }
                }
                endOfBands--;
            }
        }

        private static void Refine(ref JpegEntropyReader bits, ref int coefficient, int step)
        {
            if (bits.ReadBits(1) != 0)
            {
                coefficient += coefficient < 0 ? -step : step;
            }
        }

        private static int ReadEndOfBands(ref JpegEntropyReader bits, int size, int remaining)
        {
            int run = (1 << size) + bits.ReadBits(size);
            if (run > remaining)
            {
                throw new InvalidDataException("Progressive JPEG end-of-band run crosses the scan or restart boundary.");
            }
            return run;
        }
    }
}
