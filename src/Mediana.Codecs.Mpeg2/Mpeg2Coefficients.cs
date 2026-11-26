// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal static class Mpeg2Coefficients
    {
        private const int EndOfBlock = -1;
        private const int Escape = -2;

        public static ulong Read(scoped ref Mpeg2BitReader bits, scoped Span<int> coefficients,
            scoped ref int dc, Mpeg2PictureHeader picture, ReadOnlySpan<byte> matrix, bool chroma, bool intra, int scale)
        {
            coefficients.Clear();
            int position = -1;
            int sum = 0;
            ulong nonzero = 0;
            if (intra)
            {
                int size = (chroma ? Mpeg2Tables.ChromaDc : Mpeg2Tables.LumaDc).Read(ref bits);
                int differential = bits.Read(size);
                if (size != 0 && differential < 1 << (size - 1))
                {
                    differential -= (1 << size) - 1;
                }
                dc += differential;
                coefficients[0] = sum = Math.Clamp(dc << (3 - picture.DcPrecision), -2048, 2047);
                nonzero = sum != 0 ? 1UL : 0;
                position = 0;
            }
            else if (bits.Peek(1) == 1)
            {
                bits.Skip(1);
                int sign = bits.Flag() ? -1 : 1;
                coefficients[0] = sum = Dequantize(sign, matrix[0], scale, false);
                nonzero = sum != 0 ? 1UL : 0;
                position = 0;
            }

            ReadOnlySpan<byte> scan = picture.AlternateScan ? Mpeg2Quantization.AlternateScan : Mpeg2Quantization.ZigZag;
            Mpeg2Vlc table = intra && picture.AlternateIntraVlc ? Mpeg2Tables.DctOne : Mpeg2Tables.DctZero;
            while (true)
            {
                int symbol = table.Read(ref bits);
                if (symbol == EndOfBlock) { break; }
                int run, level;
                if (symbol == Escape)
                {
                    run = bits.Read(6);
                    level = bits.Read(12);
                    if (level >= 2048) { level -= 4096; }
                    if (level is 0 or -2048) { throw new InvalidDataException("The MPEG-2 escape coefficient level is reserved."); }
                }
                else
                {
                    run = symbol >> 6;
                    level = symbol & 63;
                    if (bits.Flag()) { level = -level; }
                }
                position += run + 1;
                if (position >= 64) { throw new InvalidDataException("The MPEG-2 coefficient run exceeds its block."); }
                int index = scan[position];
                int coefficient = Dequantize(level, matrix[index], scale, intra);
                coefficients[index] = coefficient;
                sum += coefficient;
                if (coefficient != 0) { nonzero |= 1UL << index; }
            }
            if ((sum & 1) == 0)
            {
                coefficients[63] ^= 1;
                if (coefficients[63] == 0) { nonzero &= ~(1UL << 63); }
                else { nonzero |= 1UL << 63; }
            }
            return nonzero;
        }

        private static int Dequantize(int level, int weight, int scale, bool intra)
        {
            int magnitude = Math.Abs(level);
            int weighted = intra ? magnitude * weight * scale / 16 : (2 * magnitude + 1) * weight * scale / 32;
            return Math.Clamp(level < 0 ? -weighted : weighted, -2048, 2047);
        }
    }
}
