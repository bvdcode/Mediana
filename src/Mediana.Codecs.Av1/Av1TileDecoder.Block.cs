// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void Block(int row, int column, int width, int height)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int skipContext = (row > 0 ? above[column].Skip : 0) +
                (column > 0 ? left[row & (LeftContexts - 1)].Skip : 0);
            int skip = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SkipOffset + skipContext * 3, 3));
            int aboveMode = row > 0 ? above[column].Mode : 0;
            int leftMode = column > 0 ? left[row & (LeftContexts - 1)].Mode : 0;
            ReadOnlySpan<byte> modeContexts = [0, 1, 2, 3, 4, 4, 4, 4, 3, 0, 1, 2, 0];
            int modeContext = modeContexts[aboveMode] * 5 + modeContexts[leftMode];
            int mode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.IntraModeOffset + modeContext * 14, 14));
            if (mode > 8)
            {
                throw new NotSupportedException("AV1 smooth and Paeth intra prediction are not implemented.");
            }
            int blockIndex = BlockIndex(width, height);
            int angleDelta = 0;
            if (mode != 0 && blockIndex >= 3)
            {
                angleDelta = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.AngleDeltaOffset + (mode - 1) * 8, 8)) - 3;
            }
            if (mode != 0 && sequence.IntraEdgeFilter && (mode != 1 && mode != 2 || angleDelta != 0))
            {
                throw new NotSupportedException("AV1 directional intra edge filtering is not implemented.");
            }
            Span<byte> colors = stackalloc byte[8];
            int paletteSize = 0;
            int widthLog = BitOperations.Log2((uint)width);
            int heightLog = BitOperations.Log2((uint)height);
            if (mode == 0 && blockIndex >= 3 && width <= 16 && height <= 16 && header.ScreenContent)
            {
                paletteSize = Palette(row, column, widthLog + heightLog - 2, colors);
            }
            if (mode == 0 && paletteSize == 0 && sequence.FilterIntra && width <= 8 && height <= 8)
            {
                int index = blockIndex;
                if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.FilterIntraOffset + index * 3, 3)) != 0)
                {
                    throw new NotSupportedException("AV1 filter-intra prediction is not implemented.");
                }
            }
            Span<byte> colorMap = stackalloc byte[64 * 64];
            if (paletteSize != 0)
            {
                ReadPaletteTokens(row, column, width * 4, height * 4, paletteSize, colorMap);
            }
            Residual(row, column, width, height, skip != 0, mode, angleDelta, colors[..paletteSize], colorMap);
            Av1BlockContext context = new()
            {
                Mode = (byte)mode,
                WidthLog = (byte)widthLog,
                HeightLog = (byte)heightLog,
                Skip = (byte)skip,
                PaletteCount = (byte)paletteSize
            };
            for (int index = 0; index < paletteSize; index++)
            {
                context.Palette[index] = colors[index];
            }
            for (int index = 0; index < width && column + index < sequence.MiColumns; index++)
            {
                context.Level = above[column + index].Level;
                context.Dc = above[column + index].Dc;
                above[column + index] = context;
            }
            for (int index = 0; index < height && row + index < sequence.MiRows; index++)
            {
                int previous = (row + index) & (LeftContexts - 1);
                context.Level = left[previous].Level;
                context.Dc = left[previous].Dc;
                left[previous] = context;
            }
        }

        private void Residual(int row, int column, int width, int height, bool skip, int mode, int angleDelta,
            scoped ReadOnlySpan<byte> colors, scoped ReadOnlySpan<byte> colorMap)
        {
            int codedWidth = Math.Min(width, sequence.MiColumns - column);
            int codedHeight = Math.Min(height, sequence.MiRows - row);
            Span<int> residual = stackalloc int[16];
            for (int unitY = 0; unitY < codedHeight; unitY += 16)
            {
                for (int unitX = 0; unitX < codedWidth; unitX += 16)
                {
                    for (int y = unitY; y < Math.Min(unitY + 16, codedHeight); y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int x = unitX; x < Math.Min(unitX + 16, codedWidth); x++)
                        {
                            Coefficients(column + x, row + y, width, height, skip, residual);
                            int pixelX = (column + x) * 4;
                            int pixelY = (row + y) * 4;
                            if (colors.IsEmpty)
                            {
                                PredictIntra(pixelX, pixelY, mode, angleDelta);
                            }
                            else
                            {
                                for (int dy = 0; dy < 4; dy++)
                                {
                                    Span<byte> target = output.Slice((pixelY + dy) * sequence.Stride + pixelX, 4);
                                    for (int dx = 0; dx < 4; dx++)
                                    {
                                        target[dx] = colors[colorMap[(y * 4 + dy) * (width * 4) + x * 4 + dx]];
                                    }
                                }
                            }
                            for (int dy = 0; dy < 4; dy++)
                            {
                                Span<byte> target = output.Slice((pixelY + dy) * sequence.Stride + pixelX, 4);
                                for (int dx = 0; dx < 4; dx++)
                                {
                                    target[dx] = (byte)Math.Clamp(target[dx] + residual[dy * 4 + dx], 0, 255);
                                }
                            }
                            decoded[(row + y) * sequence.MiColumns + column + x] = 1;
                        }
                    }
                }
            }
        }

        private void PredictDc(int x, int y)
        {
            int sum = 0;
            int count = 0;
            if (x > 0)
            {
                for (int index = 0; index < 4; index++)
                {
                    sum += output[(y + index) * sequence.Stride + x - 1];
                }
                count += 4;
            }
            if (y > 0)
            {
                foreach (byte sample in output.Slice((y - 1) * sequence.Stride + x, 4))
                {
                    sum += sample;
                }
                count += 4;
            }
            byte predicted = count == 0 ? (byte)128 : (byte)((sum + count / 2) / count);
            for (int index = 0; index < 4; index++)
            {
                output.Slice((y + index) * sequence.Stride + x, 4).Fill(predicted);
            }
        }

        private static int BlockIndex(int width, int height) => (width, height) switch
        {
            (1, 1) => 0,
            (1, 2) => 1,
            (2, 1) => 2,
            (2, 2) => 3,
            (2, 4) => 4,
            (4, 2) => 5,
            (4, 4) => 6,
            (4, 8) => 7,
            (8, 4) => 8,
            (8, 8) => 9,
            (8, 16) => 10,
            (16, 8) => 11,
            (16, 16) => 12,
            (16, 32) => 13,
            (32, 16) => 14,
            (32, 32) => 15,
            (1, 4) => 16,
            (4, 1) => 17,
            (2, 8) => 18,
            (8, 2) => 19,
            (4, 16) => 20,
            (16, 4) => 21,
            _ => throw new InvalidDataException("The AV1 block geometry is invalid.")
        };
    }
}
