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
            segmentId = 0;
            if (interFrame || segmentsBeforeSkip) { ReadSegment(row, column, width, height, interFrame, true, false); }
            bool skipMode = false;
            if (interFrame && header.Inter.SkipMode && Math.Min(width, height) >= 2 && !Segment.PreSkip)
            {
                int skipModeContext = (row > bounds.RowStart && motionBlocks[(row - 1) * sequence.MiColumns + column].SkipMode ? 1 : 0) +
                    (column > bounds.ColumnStart && motionBlocks[row * sequence.MiColumns + column - 1].SkipMode ? 1 : 0);
                skipMode = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SkipModeOffset + skipModeContext * 3, 3)) != 0;
            }
            int skipContext = (row > bounds.RowStart ? above[column].Skip : 0) +
                (column > bounds.ColumnStart ? left[row & (LeftContexts - 1)].Skip : 0);
            int skip = skipMode || segmentsBeforeSkip && Segment.Skip ? 1 : symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.SkipOffset + skipContext * 3, 3));
            if (!segmentsBeforeSkip) { ReadSegment(row, column, width, height, interFrame, false, skip != 0); }
            StoreSegment(row, column, width, height);
            lossless = (header.Quantization with { BaseIndex = Segment.QuantizerIndex(header.Quantization.BaseIndex) }).Lossless;
            ReadCdef(row, column, width, height, skip != 0);
            ReadBlockDeltas(width, height, skip != 0);
            quantization = header.Quantization with { BaseIndex = Segment.QuantizerIndex(currentQuantizer) };
            if (interFrame && (skipMode || ReadInter(row, column)))
            {
                InterBlock(row, column, width, height, skip, skipMode);
                return;
            }
            int aboveMode = row > bounds.RowStart ? above[column].Mode : 0;
            int leftMode = column > bounds.ColumnStart ? left[row & (LeftContexts - 1)].Mode : 0;
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
            bool hasChroma = !sequence.Monochrome && !(height == 1 && (row & 1) == 0) &&
                !(width == 1 && (column & 1) == 0);
            Av1ChromaMode chroma = hasChroma ? ReadChromaMode(mode, blockIndex, width, height) : default;
            Span<byte> colors = stackalloc byte[8];
            int paletteSize = 0;
            Span<byte> chromaColors = stackalloc byte[16];
            int chromaPaletteSize = 0;
            int widthLog = BitOperations.Log2((uint)width);
            int heightLog = BitOperations.Log2((uint)height);
            if (mode == 0 && blockIndex >= 3 && width <= 16 && height <= 16 && header.ScreenContent)
            {
                paletteSize = Palette(row, column, widthLog + heightLog - 2, colors);
            }
            if (hasChroma && chroma.Mode == 0 && blockIndex >= 3 && width <= 16 && height <= 16 && header.ScreenContent)
            {
                chromaPaletteSize = ChromaPalette(row, column, widthLog + heightLog - 2, paletteSize, chromaColors);
            }
            int? filter = null;
            if (mode == 0 && paletteSize == 0 && sequence.FilterIntra && width <= 8 && height <= 8)
            {
                int index = blockIndex;
                if (symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.FilterIntraOffset + index * 3, 3)) != 0)
                {
                    filter = symbols.ReadSymbol(probabilities.Slice(Av1DefaultCdfs.FilterIntraModeOffset, 6));
                }
            }
            Span<byte> colorMap = paletteSize == 0 ? default : stackalloc byte[width * height * 16];
            Span<byte> chromaMap = chromaPaletteSize == 0 ? default : stackalloc byte[Math.Max(4, width * 2) * Math.Max(4, height * 2)];
            if (paletteSize != 0)
            {
                ReadPaletteTokens(row, column, width * 4, height * 4, paletteSize, colorMap);
            }
            if (chromaPaletteSize != 0) { ReadPaletteTokens(row, column, width * 4, height * 4, chromaPaletteSize, chromaMap, 1); }
            StoreFilterBlock(row, column, width, height, 0, mode, skip != 0);
            Av1TransformSize transform = ReadTransformSize(row, column, width, height, true);
            Residual(row, column, width, height, skip != 0, new(mode, angleDelta, filter), hasChroma, chroma, new(colors[..paletteSize], chromaColors[..(chromaPaletteSize * 2)], colorMap, chromaMap), transform);
            Av1BlockContext context = new()
            {
                Mode = (byte)mode,
                WidthLog = (byte)widthLog,
                HeightLog = (byte)heightLog,
                Skip = (byte)skip,
                TransformWidth = (byte)transform.Width,
                TransformHeight = (byte)transform.Height,
                PaletteCount = (byte)paletteSize, UvPaletteCount = (byte)chromaPaletteSize
            };
            for (int index = 0; index < paletteSize; index++)
            {
                context.Palette[index] = colors[index];
            }
            for (int index = 0; index < chromaPaletteSize; index++) { context.UvPalette[index] = chromaColors[index]; }
            StoreContext(row, column, width, height, context);
            StoreMotion(row, column, width, height, new() { Width = (byte)width, Height = (byte)height, TransformSize = (byte)transform.Index });
        }

        private void StoreContext(int row, int column, int width, int height, Av1BlockContext context)
        {
            for (int index = 0; index < width && column + index < sequence.MiColumns; index++)
            {
                context.SegmentPredicted = above[column + index].SegmentPredicted;
                context.Level = above[column + index].Level;
                context.Dc = above[column + index].Dc;
                above[column + index] = context;
            }
            for (int index = 0; index < height && row + index < sequence.MiRows; index++)
            {
                int previous = (row + index) & (LeftContexts - 1);
                context.SegmentPredicted = left[previous].SegmentPredicted;
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
