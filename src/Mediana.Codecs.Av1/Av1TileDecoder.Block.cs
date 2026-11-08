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
            bool interFrame = header.FrameType is Av1FrameType.Inter or Av1FrameType.Switch;
            if (interFrame && header.Inter.SkipMode && Math.Min(width, height) >= 2 &&
                symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SkipModeOffset, 3)) != 0)
            {
                throw new NotSupportedException("AV1 compound skip-mode reconstruction is not implemented.");
            }
            int skipContext = (row > 0 ? above[column].Skip : 0) +
                (column > 0 ? left[row & (LeftContexts - 1)].Skip : 0);
            int skip = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SkipOffset + skipContext * 3, 3));
            ReadCdef(row, column, width, height, skip != 0);
            if (interFrame && ReadInter(row, column))
            {
                InterBlock(row, column, width, height, skip);
                return;
            }
            int aboveMode = row > 0 ? above[column].Mode : 0;
            int leftMode = column > 0 ? left[row & (LeftContexts - 1)].Mode : 0;
            ReadOnlySpan<byte> modeContexts = [0, 1, 2, 3, 4, 4, 4, 4, 3, 0, 1, 2, 0];
            int modeContext = modeContexts[aboveMode] * 5 + modeContexts[leftMode];
            int modeOffset = Av1DefaultCdfs.IntraModeOffset + modeContext * 14;
            if (interFrame)
            {
                int sizeGroup = BlockSizeGroup(BlockIndex(width, height));
                modeOffset = Av1DefaultCdfs.InterIntraModeOffset + sizeGroup * 14;
            }
            int mode = symbols.ReadSymbol(probabilities.Slice(modeOffset, 14));
            int blockIndex = BlockIndex(width, height);
            int angleDelta = 0;
            if (mode is >= 1 and <= 8 && blockIndex >= 3)
            {
                angleDelta = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.AngleDeltaOffset + (mode - 1) * 8, 8)) - 3;
            }
            if (mode is >= 1 and <= 8 && sequence.IntraEdgeFilter && (mode != 1 && mode != 2 || angleDelta != 0))
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
            StoreFilterBlock(row, column, width, height, 0, mode, skip != 0);
            Av1TransformSize transform = ReadTransformSize(row, column, width, height, true);
            Residual(row, column, width, height, skip != 0, mode, angleDelta, hasChroma, chroma, colors[..paletteSize], colorMap, transform);
            Av1BlockContext context = new()
            {
                Mode = (byte)mode,
                WidthLog = (byte)widthLog,
                HeightLog = (byte)heightLog,
                Skip = (byte)skip,
                TransformWidth = (byte)transform.Width,
                TransformHeight = (byte)transform.Height,
                PaletteCount = (byte)paletteSize
            };
            for (int index = 0; index < paletteSize; index++)
            {
                context.Palette[index] = colors[index];
            }
            StoreContext(row, column, width, height, context);
            StoreMotion(row, column, width, height, new() { Width = (byte)width, Height = (byte)height, TransformSize = (byte)transform.Index });
        }

        private void StoreContext(int row, int column, int width, int height, Av1BlockContext context)
        {
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

        private static int BlockSizeGroup(int blockIndex)
        {
            ReadOnlySpan<byte> groups = [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 0, 0, 1, 1, 2, 2];
            return groups[blockIndex];
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
