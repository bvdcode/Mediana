// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void Coefficients(int x4, int y4, int blockWidth, int blockHeight, bool skip, scoped Span<int> quant)
        {
            quant.Clear();
            ref Av1BlockContext top = ref above[x4];
            ref Av1BlockContext prior = ref left[y4 & (LeftContexts - 1)];
            int context;
            if (blockWidth == 1 && blockHeight == 1)
            {
                context = 0;
            }
            else if (top.Level == 0 && prior.Level == 0)
            {
                context = 1;
            }
            else if (top.Level == 0 || prior.Level == 0)
            {
                context = 2 + (Math.Max(top.Level, prior.Level) > 3 ? 1 : 0);
            }
            else if (Math.Max(top.Level, prior.Level) <= 3)
            {
                context = 4;
            }
            else
            {
                context = Math.Min(top.Level, prior.Level) <= 3 ? 5 : 6;
            }
            if (skip || symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.AllZeroOffset + context * 3, 3)) != 0)
            {
                top.Level = prior.Level = top.Dc = prior.Dc = 0;
                return;
            }
            int point = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.Eob16Offset, 6)) + 1;
            int end = point < 2 ? point : (1 << (point - 2)) + 1;
            int shift = point - 3;
            if (shift >= 0)
            {
                int extra = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.EobExtraOffset + shift * 3, 3));
                end += extra << shift;
                end += (int)symbols.ReadLiteral(shift);
            }
            ReadOnlySpan<byte> scan = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
            for (int ordinal = end - 1; ordinal >= 0; ordinal--)
            {
                int position = scan[ordinal];
                int level;
                if (ordinal == end - 1)
                {
                    int lastContext = ordinal switch { 0 => 0, <= 2 => 1, <= 4 => 2, _ => 3 };
                    level = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBaseEobOffset + lastContext * 4, 4)) + 1;
                }
                else
                {
                    int baseContext = CoefficientBaseContext(quant, position);
                    level = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBaseOffset + baseContext * 5, 5));
                }
                if (level > 2)
                {
                    int rangeContext = CoefficientRangeContext(quant, position);
                    for (int index = 0; index < 4; index++)
                    {
                        int extra = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBrOffset + rangeContext * 5, 5));
                        level += extra;
                        if (extra < 3)
                        {
                            break;
                        }
                    }
                }
                quant[position] = level;
            }
            int total = 0;
            byte dc = 0;
            for (int ordinal = 0; ordinal < end; ordinal++)
            {
                int position = scan[ordinal];
                int level = quant[position];
                if (level == 0)
                {
                    continue;
                }
                bool negative;
                if (ordinal == 0)
                {
                    int sum = DcSign(top.Dc) + DcSign(prior.Dc);
                    int signContext = sum switch { < 0 => 1, > 0 => 2, _ => 0 };
                    negative = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.DcSignOffset + signContext * 3, 3)) != 0;
                }
                else
                {
                    negative = symbols.ReadBoolean();
                }
                if (level > 14)
                {
                    level = ReadCoefficientEscape() + 14;
                }
                if (position == 0)
                {
                    dc = negative ? (byte)1 : (byte)2;
                }
                level &= 0xFFFFF;
                total += level;
                int signed = negative ? -level : level;
                quant[position] = Math.Clamp(signed * 4, short.MinValue, short.MaxValue);
            }
            top.Level = prior.Level = (byte)Math.Min(63, total);
            top.Dc = prior.Dc = dc;
            Av1LosslessTransform.Inverse(quant);
        }

        private int ReadCoefficientEscape()
        {
            int length = 1;
            while (!symbols.ReadBoolean())
            {
                if (length == 20)
                {
                    throw new InvalidDataException("The AV1 coefficient Golomb code exceeds 20 bits.");
                }
                length++;
            }
            return (1 << (length - 1)) | (int)symbols.ReadLiteral(length - 1);
        }

        private static int DcSign(int value) => value switch
        {
            0 => 0,
            1 => -1,
            2 => 1,
            _ => throw new InvalidDataException("The AV1 DC sign context is invalid.")
        };

        private static int CoefficientBaseContext(scoped ReadOnlySpan<int> levels, int position)
        {
            if (position == 0)
            {
                return 0;
            }
            int row = position >> 2;
            int column = position & 3;
            int magnitude = Neighbor(levels, row, column + 1, 3) + Neighbor(levels, row + 1, column, 3) +
                Neighbor(levels, row + 1, column + 1, 3) + Neighbor(levels, row, column + 2, 3) +
                Neighbor(levels, row + 2, column, 3);
            ReadOnlySpan<byte> offsets = [0, 1, 6, 6, 1, 6, 6, 21, 6, 6, 21, 21, 6, 21, 21, 21];
            return Math.Min((magnitude + 1) >> 1, 4) + offsets[position];
        }

        private static int CoefficientRangeContext(scoped ReadOnlySpan<int> levels, int position)
        {
            int row = position >> 2;
            int column = position & 3;
            int magnitude = Neighbor(levels, row, column + 1, 15) + Neighbor(levels, row + 1, column, 15) +
                Neighbor(levels, row + 1, column + 1, 15);
            int context = Math.Min((magnitude + 1) >> 1, 6);
            if (position == 0)
            {
                return context;
            }
            return context + (row < 2 && column < 2 ? 7 : 14);
        }

        private static int Neighbor(scoped ReadOnlySpan<int> levels, int row, int column, int maximum)
        {
            return row < 4 && column < 4 ? Math.Min(levels[row * 4 + column], maximum) : 0;
        }
    }
}
