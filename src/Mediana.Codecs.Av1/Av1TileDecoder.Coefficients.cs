// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void Coefficients(int plane, int x4, int y4, int blockWidth, int blockHeight, bool skip, int mode, scoped Span<int> quant, bool inter = false, int lumaRow = 0, int lumaColumn = 0, int transformWidth = 4, int transformHeight = 4)
        {
            quant.Clear();
            Av1TransformSize size = new(transformWidth, transformHeight);
            int planeType = plane == 0 ? 0 : 1;
            int context = CoefficientSkipContext(plane, x4, y4, blockWidth, blockHeight, size);
            if (skip || symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.AllZeroOffset + size.Context * 39 + context * 3, 3)) != 0)
            {
                StoreCoefficientContext(plane, x4, y4, size, 0, 0);
                StoreTransform(plane, x4, y4, size, 0);
                return;
            }
            int transform = ReadTransform(plane, mode, inter, x4, y4, lumaRow, lumaColumn, size);
            StoreTransform(plane, x4, y4, size, transform);
            int transformClass = transform switch { 10 or 12 or 14 => 2, 11 or 13 or 15 => 1, _ => 0 };
            int eobSize = System.Numerics.BitOperations.Log2((uint)(size.CodedWidth * size.CodedHeight)) - 4;
            int eobOffset = eobSize switch
            {
                0 => Av1DefaultCdfs.Eob16Offset, 1 => Av1DefaultCdfs.Eob32Offset,
                2 => Av1DefaultCdfs.Eob64Offset, 3 => Av1DefaultCdfs.Eob128Offset,
                4 => Av1DefaultCdfs.Eob256Offset, 5 => Av1DefaultCdfs.Eob512Offset,
                6 => Av1DefaultCdfs.Eob1024Offset,
                _ => throw new InvalidDataException("The AV1 coefficient extent is invalid.")
            };
            int alphabet = 6 + eobSize;
            int eobContext = eobSize >= 5 ? planeType : planeType * 2 + (transformClass == 0 ? 0 : 1);
            int point = symbols.ReadSymbol(probabilities.Slice(eobOffset + eobContext * alphabet, alphabet)) + 1;
            int end = point < 2 ? point : (1 << (point - 2)) + 1;
            int shift = point - 3;
            if (shift >= 0)
            {
                int extra = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.EobExtraOffset + size.Context * 54 + planeType * 27 + shift * 3, 3));
                end += extra << shift;
                end += (int)symbols.ReadLiteral(shift);
            }
            ReadOnlySpan<ushort> scan = Av1CoefficientTables.Scan(size.CodedWidth, size.CodedHeight);
            for (int ordinal = end - 1; ordinal >= 0; ordinal--)
            {
                int position = ScanPosition(scan, ordinal, transformClass, size);
                int level;
                if (ordinal == end - 1)
                {
                    int area = size.CodedWidth * size.CodedHeight;
                    int lastContext = ordinal == 0 ? 0 : ordinal <= area / 8 ? 1 : ordinal <= area / 4 ? 2 : 3;
                    level = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBaseEobOffset + size.Context * 32 + planeType * 16 + lastContext * 4, 4)) + 1;
                }
                else
                {
                    int baseContext = CoefficientBaseContext(quant, position, transformClass, size);
                    level = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBaseOffset + size.Context * 420 + planeType * 210 + baseContext * 5, 5));
                }
                if (level > 2)
                {
                    int rangeContext = CoefficientRangeContext(quant, position, transformClass, size);
                    for (int index = 0; index < 4; index++)
                    {
                        int extra = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.CoeffBrOffset + Math.Min(size.Context, 3) * 210 + planeType * 105 + rangeContext * 5, 5));
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
                int position = ScanPosition(scan, ordinal, transformClass, size);
                int level = quant[position];
                if (level == 0)
                {
                    continue;
                }
                bool negative;
                if (ordinal == 0)
                {
                    int sum = CoefficientDcSign(plane, x4, y4, size);
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
                int multiplier = position == 0 ? quantization.Dc(plane) : quantization.Ac(plane);
                int dequantized = (Math.Abs(signed * multiplier) & 0xFFFFFF) >> size.DequantizationShift;
                quant[position] = Math.Clamp(negative ? -dequantized : dequantized, short.MinValue, short.MaxValue);
            }
            StoreCoefficientContext(plane, x4, y4, size, (byte)Math.Min(63, total), dc);
            if (size.Width != size.CodedWidth)
            {
                for (int row = size.CodedHeight - 1; row >= 0; row--)
                {
                    quant.Slice(row * size.CodedWidth, size.CodedWidth).CopyTo(quant.Slice(row * size.Width));
                    quant.Slice(row * size.Width + size.CodedWidth, size.Width - size.CodedWidth).Clear();
                }
            }
            if (lossless)
            {
                Av1LosslessTransform.Inverse(quant);
            }
            else
            {
                Av1InverseTransform.Inverse(quant, size.Width, size.Height, transform);
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

        private static int ScanPosition(ReadOnlySpan<ushort> scan, int ordinal, int transformClass, Av1TransformSize size) => transformClass switch
        {
            0 => scan[ordinal],
            1 => (ordinal % size.CodedHeight) * size.CodedWidth + ordinal / size.CodedHeight,
            2 => ordinal,
            _ => throw new InvalidDataException("The AV1 transform class is invalid.")
        };
    }
}
