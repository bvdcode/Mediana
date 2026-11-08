// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void Coefficients(int plane, int x4, int y4, int blockWidth, int blockHeight, bool skip, int mode, scoped Span<int> quant, bool inter = false, int lumaRow = 0, int lumaColumn = 0)
        {
            quant.Clear();
            ref Av1BlockContext top = ref CoefficientAbove(plane)[x4];
            int mask = (LeftContexts >> sequence.PlaneShift(plane)) - 1;
            ref Av1BlockContext prior = ref CoefficientLeft(plane)[y4 & mask];
            int planeType = plane == 0 ? 0 : 1;
            int context;
            if (plane != 0)
            {
                context = 7 + ((top.Level | top.Dc) != 0 ? 1 : 0) + ((prior.Level | prior.Dc) != 0 ? 1 : 0);
                if (blockWidth * blockHeight > 1)
                {
                    context += 3;
                }
            }
            else if (blockWidth == 1 && blockHeight == 1)
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
            int transform = ReadTransform(plane, mode, inter, x4, y4, lumaRow, lumaColumn);
            if (inter && plane == 0) { motionBlocks[y4 * sequence.MiColumns + x4].Transform = (byte)transform; }
            int transformClass = transform switch { 10 or 12 or 14 => 2, 11 or 13 or 15 => 1, _ => 0 };
            int point = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.Eob16Offset + planeType * 12 + (transformClass == 0 ? 0 : 6), 6)) + 1;
            int end = point < 2 ? point : (1 << (point - 2)) + 1;
            int shift = point - 3;
            if (shift >= 0)
            {
                int extra = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.EobExtraOffset + planeType * 27 + shift * 3, 3));
                end += extra << shift;
                end += (int)symbols.ReadLiteral(shift);
            }
            ReadOnlySpan<byte> scan;
            switch (transformClass)
            {
                case 0: scan = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15]; break;
                case 1: scan = [0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15]; break;
                case 2: scan = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]; break;
                default: throw new InvalidDataException("The AV1 transform class is invalid.");
            }
            for (int ordinal = end - 1; ordinal >= 0; ordinal--)
            {
                int position = scan[ordinal];
                int level;
                if (ordinal == end - 1)
                {
                    int lastContext = ordinal switch { 0 => 0, <= 2 => 1, <= 4 => 2, _ => 3 };
                    level = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBaseEobOffset + planeType * 16 + lastContext * 4, 4)) + 1;
                }
                else
                {
                    int baseContext = CoefficientBaseContext(quant, position, transformClass);
                    level = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBaseOffset + planeType * 210 + baseContext * 5, 5));
                }
                if (level > 2)
                {
                    int rangeContext = CoefficientRangeContext(quant, position, transformClass);
                    for (int index = 0; index < 4; index++)
                    {
                        int extra = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBrOffset + planeType * 105 + rangeContext * 5, 5));
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
                    negative = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.DcSignOffset + planeType * 9 + signContext * 3, 3)) != 0;
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
                int multiplier = position == 0 ? header.Quantization.Dc(plane) : header.Quantization.Ac(plane);
                int dequantized = Math.Abs(signed * multiplier) & 0xFFFFFF;
                quant[position] = Math.Clamp(negative ? -dequantized : dequantized, short.MinValue, short.MaxValue);
            }
            top.Level = prior.Level = (byte)Math.Min(63, total);
            top.Dc = prior.Dc = dc;
            if (header.Quantization.Lossless)
            {
                Av1LosslessTransform.Inverse(quant);
            }
            else
            {
                Av1Transform4.Inverse(quant, transform);
            }
        }

        private int ReadTransform(int plane, int mode, bool inter, int x4, int y4, int lumaRow, int lumaColumn)
        {
            if (header.Quantization.Lossless)
            {
                return 0;
            }
            if (inter)
            {
                if (plane != 0)
                {
                    int row = Math.Max(lumaRow, y4 << 1);
                    int column = Math.Max(lumaColumn, x4 << 1);
                    return motionBlocks[row * sequence.MiColumns + column].Transform;
                }
                if (header.Quantization.BaseIndex == 0) { return 0; }
                if (header.ReducedTransformSet)
                {
                    int type = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterTx3Offset, 3));
                    return type == 0 ? 9 : 0;
                }
                int symbol = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.InterTx1Offset, 17));
                ReadOnlySpan<byte> transforms = [9, 10, 11, 12, 13, 14, 15, 0, 1, 2, 4, 5, 3, 6, 7, 8];
                return transforms[symbol];
            }
            if (plane != 0)
            {
                return mode switch
                {
                    0 or 3 or 13 => 0,
                    1 or 5 or 8 => 1,
                    2 or 6 or 7 => 2,
                    4 => 3,
                    _ => throw new NotSupportedException("The AV1 chroma transform mode is not implemented.")
                };
            }
            if (header.Quantization.BaseIndex == 0)
            {
                return 0;
            }
            if (header.ReducedTransformSet)
            {
                int symbol = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IntraTx2Offset + mode * 6, 6));
                ReadOnlySpan<byte> transforms = [9, 0, 3, 1, 2];
                return transforms[symbol];
            }
            else
            {
                int symbol = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IntraTx1Offset + mode * 8, 8));
                ReadOnlySpan<byte> transforms = [9, 0, 10, 11, 3, 1, 2];
                return transforms[symbol];
            }
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

        private static int CoefficientBaseContext(scoped ReadOnlySpan<int> levels, int position, int transformClass)
        {
            if (position == 0 && transformClass == 0)
            {
                return 0;
            }
            int row = position >> 2;
            int column = position & 3;
            if (transformClass != 0)
            {
                int extent = transformClass == 1 ? column : row;
                int sum = Neighbor(levels, row, column + 1, 3) + Neighbor(levels, row + 1, column, 3);
                for (int step = 2; step <= 4; step++)
                {
                    sum += transformClass == 1 ? Neighbor(levels, row, column + step, 3) : Neighbor(levels, row + step, column, 3);
                }
                return Math.Min((sum + 1) >> 1, 4) + 26 + Math.Min(extent, 2) * 5;
            }
            int magnitude = Neighbor(levels, row, column + 1, 3) + Neighbor(levels, row + 1, column, 3) +
                Neighbor(levels, row + 1, column + 1, 3) + Neighbor(levels, row, column + 2, 3) +
                Neighbor(levels, row + 2, column, 3);
            ReadOnlySpan<byte> offsets = [0, 1, 6, 6, 1, 6, 6, 21, 6, 6, 21, 21, 6, 21, 21, 21];
            return Math.Min((magnitude + 1) >> 1, 4) + offsets[position];
        }

        private static int CoefficientRangeContext(scoped ReadOnlySpan<int> levels, int position, int transformClass)
        {
            int row = position >> 2;
            int column = position & 3;
            int magnitude = Neighbor(levels, row, column + 1, 15) + Neighbor(levels, row + 1, column, 15) +
                (transformClass switch
                {
                    0 => Neighbor(levels, row + 1, column + 1, 15),
                    1 => Neighbor(levels, row, column + 2, 15),
                    2 => Neighbor(levels, row + 2, column, 15),
                    _ => throw new InvalidDataException("The AV1 transform class is invalid.")
                });
            int context = Math.Min((magnitude + 1) >> 1, 6);
            if (position == 0)
            {
                return context;
            }
            bool near = transformClass switch
            {
                0 => row < 2 && column < 2,
                1 => column == 0,
                2 => row == 0,
                _ => throw new InvalidDataException("The AV1 transform class is invalid.")
            };
            return context + (near ? 7 : 14);
        }

        private static int Neighbor(scoped ReadOnlySpan<int> levels, int row, int column, int maximum)
        {
            return row < 4 && column < 4 ? Math.Min(levels[row * 4 + column], maximum) : 0;
        }
    }
}
