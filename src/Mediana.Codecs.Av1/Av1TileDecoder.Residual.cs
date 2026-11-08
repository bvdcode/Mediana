// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal ref partial struct Av1TileDecoder
    {
        private void Residual(int row, int column, int width, int height, bool skip, Av1IntraMode luma,
            bool hasChroma, Av1ChromaMode chroma, scoped ReadOnlySpan<byte> colors, scoped ReadOnlySpan<byte> colorMap,
            Av1TransformSize lumaTransform, bool inter = false)
        {
            int capacity = lumaTransform.Width * lumaTransform.Height;
            if (hasChroma && !header.Quantization.Lossless)
            {
                capacity = Math.Max(capacity, Math.Min(Math.Max(4, width * 2), 32) * Math.Min(Math.Max(4, height * 2), 32));
            }
            Span<int> residual = stackalloc int[capacity];
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
                        Av1TransformSize size = plane == 0 ? lumaTransform : header.Quantization.Lossless ? new(4, 4) :
                            new(Math.Min(blockWidth * 4, 32), Math.Min(blockHeight * 4, 32));
                        if (inter && plane == 0 && !header.Quantization.Lossless)
                        {
                            InterTransformTree(row, column, width, height, startX, startY,
                                Math.Min(16, width - startX) * 4, Math.Min(16, height - startY) * 4, skip, residual);
                            continue;
                        }
                        for (int y = startY; y < endY; y += size.Height4)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            for (int x = startX; x < endX; x += size.Width4)
                            {
                                TransformBlock(plane, row, column, width, height, x, y, blockWidth, blockHeight,
                                    skip, plane == 0 ? luma : new(chroma.Mode, chroma.AngleDelta), chroma, colors, colorMap, size, residual[..(size.Width * size.Height)], inter);
                            }
                        }
                    }
                }
            }
        }

        private void TransformBlock(int plane, int row, int column, int width, int height, int x, int y, int blockWidth, int blockHeight,
            bool skip, Av1IntraMode prediction, Av1ChromaMode chroma, scoped ReadOnlySpan<byte> colors,
            scoped ReadOnlySpan<byte> colorMap, Av1TransformSize size, scoped Span<int> residual, bool inter)
        {
            int sub = sequence.PlaneShift(plane);
            int x4 = (column >> sub) + x;
            int y4 = (row >> sub) + y;
            int mode = prediction.Mode;
            Coefficients(plane, x4, y4, blockWidth, blockHeight, skip, prediction.TransformMode, residual, inter, row, column, size.Width, size.Height);
            int pixelX = x4 * 4;
            int pixelY = y4 * 4;
            int stride = sequence.PlaneStride(plane);
            int offset = sequence.PlaneOffset(plane);
            int visibleWidth = Math.Min(size.Width, stride - pixelX);
            int visibleHeight = Math.Min(size.Height, sequence.PlaneHeight(plane) - pixelY);
            if (plane == 0 && !colors.IsEmpty)
            {
                for (int dy = 0; dy < visibleHeight; dy++)
                {
                    Span<byte> target = output.Slice(offset + (pixelY + dy) * stride + pixelX, visibleWidth);
                    for (int dx = 0; dx < visibleWidth; dx++)
                    {
                        target[dx] = colors[colorMap[(y * 4 + dy) * (width * 4) + x * 4 + dx]];
                    }
                }
            }
            else if (!inter)
            {
                PredictIntra(plane, pixelX, pixelY, mode == 13 ? default : prediction, size.Width, size.Height, row, column);
                if (mode == 13)
                {
                    int alpha = plane switch
                    {
                        1 => chroma.AlphaU,
                        2 => chroma.AlphaV,
                        _ => throw new InvalidDataException("AV1 CFL prediction requires a chroma plane.")
                    };
                    int maximumX = Math.Min((column + width) * 4, sequence.Stride);
                    int maximumY = Math.Min((row + height) * 4, sequence.PaddedHeight);
                    PredictChromaFromLuma(plane, pixelX, pixelY, alpha, maximumX, maximumY, size.Width, size.Height);
                }
            }
            for (int dy = 0; dy < visibleHeight; dy++)
            {
                Span<byte> target = output.Slice(offset + (pixelY + dy) * stride + pixelX, visibleWidth);
                for (int dx = 0; dx < visibleWidth; dx++)
                {
                    target[dx] = (byte)Math.Clamp(target[dx] + residual[dy * size.Width + dx], 0, 255);
                }
            }
            StoreFilterTransform(plane, x4, y4, size);
            byte decodedFlags = DecodedFlag;
            if (!inter && mode is >= 9 and <= 11) { decodedFlags |= SmoothFlag; }
            for (int dy = 0; dy < (visibleHeight >> 2); dy++)
            {
                int decodedOffset = offset / 16 + (y4 + dy) * sequence.PlaneColumns(plane) + x4;
                decoded.Slice(decodedOffset, visibleWidth >> 2).Fill(decodedFlags);
            }
        }
    }
}
