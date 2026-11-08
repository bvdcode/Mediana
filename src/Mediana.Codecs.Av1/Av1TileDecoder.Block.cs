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
            bool hasChroma = !sequence.Monochrome && !(height == 1 && (row & 1) == 0) &&
                !(width == 1 && (column & 1) == 0);
            Av1ChromaMode chroma = hasChroma ? ReadChromaMode(mode, blockIndex, width, height) : default;
            Span<byte> colors = stackalloc byte[8];
            int paletteSize = 0;
            int widthLog = BitOperations.Log2((uint)width);
            int heightLog = BitOperations.Log2((uint)height);
            if (mode == 0 && blockIndex >= 3 && width <= 16 && height <= 16 && header.ScreenContent)
            {
                paletteSize = Palette(row, column, widthLog + heightLog - 2, colors);
            }
            if (hasChroma && chroma.Mode == 0 && blockIndex >= 3 && width <= 16 && height <= 16 && header.ScreenContent)
            {
                int uvPaletteContext = paletteSize == 0 ? 0 : 1;
                if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.UvPaletteModeOffset + uvPaletteContext * 3, 3)) != 0)
                {
                    throw new NotSupportedException("AV1 chroma palette reconstruction is not implemented.");
                }
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
            if (!header.Quantization.Lossless && (width != 1 || height != 1))
            {
                throw new NotSupportedException("AV1 quantized transform sizes larger than four samples are not implemented.");
            }
            Residual(row, column, width, height, skip != 0, mode, angleDelta, hasChroma, chroma, colors[..paletteSize], colorMap);
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
            bool hasChroma, Av1ChromaMode chroma, scoped ReadOnlySpan<byte> colors, scoped ReadOnlySpan<byte> colorMap)
        {
            Span<int> residual = stackalloc int[16];
            int planes = hasChroma ? 3 : 1;
            for (int unitY = 0; unitY < height; unitY += 16)
            {
                for (int unitX = 0; unitX < width; unitX += 16)
                {
                    for (int plane = 0; plane < planes; plane++)
                    {
                        int sub = sequence.PlaneShift(plane);
                        int planeColumn = column >> sub;
                        int planeRow = row >> sub;
                        int blockWidth = Math.Max(1, width >> sub);
                        int blockHeight = Math.Max(1, height >> sub);
                        int codedWidth = Math.Min(blockWidth, sequence.PlaneColumns(plane) - planeColumn);
                        int codedHeight = Math.Min(blockHeight, sequence.PlaneRows(plane) - planeRow);
                        int startX = unitX >> sub;
                        int startY = unitY >> sub;
                        int endX = Math.Min(startX + (16 >> sub), codedWidth);
                        int endY = Math.Min(startY + (16 >> sub), codedHeight);
                        int stride = sequence.PlaneStride(plane);
                        int offset = sequence.PlaneOffset(plane);
                        int selectedMode = plane == 0 ? mode : chroma.Mode;
                        int selectedDelta = plane == 0 ? angleDelta : chroma.AngleDelta;
                        for (int y = startY; y < endY; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            for (int x = startX; x < endX; x++)
                            {
                                Coefficients(plane, planeColumn + x, planeRow + y, blockWidth, blockHeight, skip, selectedMode, residual);
                                int pixelX = (planeColumn + x) * 4;
                                int pixelY = (planeRow + y) * 4;
                                if (plane == 0 && !colors.IsEmpty)
                                {
                                    for (int dy = 0; dy < 4; dy++)
                                    {
                                        Span<byte> target = output.Slice(offset + (pixelY + dy) * stride + pixelX, 4);
                                        for (int dx = 0; dx < 4; dx++)
                                        {
                                            target[dx] = colors[colorMap[(y * 4 + dy) * (width * 4) + x * 4 + dx]];
                                        }
                                    }
                                }
                                else
                                {
                                    PredictIntra(plane, pixelX, pixelY, selectedMode == 13 ? 0 : selectedMode, selectedDelta);
                                    if (selectedMode == 13)
                                    {
                                        int alpha = plane switch
                                        {
                                            1 => chroma.AlphaU,
                                            2 => chroma.AlphaV,
                                            _ => throw new InvalidDataException("AV1 CFL prediction requires a chroma plane.")
                                        };
                                        int maximumX = Math.Min((column + width) * 4, sequence.Stride);
                                        int maximumY = Math.Min((row + height) * 4, sequence.PaddedHeight);
                                        PredictChromaFromLuma(plane, pixelX, pixelY, alpha, maximumX, maximumY);
                                    }
                                }
                                for (int dy = 0; dy < 4; dy++)
                                {
                                    Span<byte> target = output.Slice(offset + (pixelY + dy) * stride + pixelX, 4);
                                    for (int dx = 0; dx < 4; dx++)
                                    {
                                        target[dx] = (byte)Math.Clamp(target[dx] + residual[dy * 4 + dx], 0, 255);
                                    }
                                }
                                int decodedOffset = offset / 16 + (planeRow + y) * sequence.PlaneColumns(plane) + planeColumn + x;
                                decoded[decodedOffset] = 1;
                            }
                        }
                    }
                }
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
