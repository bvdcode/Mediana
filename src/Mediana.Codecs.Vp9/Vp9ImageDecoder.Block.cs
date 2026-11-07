// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal ref partial struct Vp9ImageDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private void Block(ref Vp9BooleanReader bits, int row, int column, int size)
        {
            bool hasAbove = row > 0, hasLeft = column > tile.ColumnStart;
            Vp9BlockInfo above = hasAbove ? blocks[(row - 1) * miStride + column] : default;
            Vp9BlockInfo left = hasLeft ? blocks[row * miStride + column - 1] : default;
            int segment = Segment(ref bits, row, column, size);
            bool skip = header.Segmentation && state.SegmentEnabled[segment * 4 + 3] || symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.Skip + (above.Skip ? 1 : 0) + (left.Skip ? 1 : 0));
            bool inter = !header.IsIntra && (header.Segmentation && state.SegmentEnabled[segment * 4 + 2]
                ? state.SegmentFeatures[segment * 4 + 2] != 0 : symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.IsInter +
                    Vp9ReferenceContext.IsInter(above, left, hasAbove, hasLeft)));
            int maxTransform = Math.Min(BitOperations.Log2((uint)Widths[size]), BitOperations.Log2((uint)Heights[size])) - 2;
            maxTransform = Math.Min(3, maxTransform);
            int transform = Math.Min(maxTransform, Math.Min(compressed.TransformMode, 3));
            if ((!skip || !inter) && compressed.TransformMode == 4 && size >= 3)
            {
                int upper = hasAbove && !above.Skip ? above.Transform : maxTransform, lower = hasLeft && !left.Skip ? left.Transform : maxTransform;
                if (!hasLeft) { lower = upper; } if (!hasAbove) { upper = lower; }
                int context = upper + lower > maxTransform ? 1 : 0; transform = 0;
                while (transform < maxTransform && symbols.Read(ref bits, probabilities, Vp9ProbabilityOffset.Transform + maxTransform * 6 + context * 3 + transform)) { transform++; }
            }
            if (inter)
            {
                Vp9BlockInfo motion = new() { Size = (byte)size, Transform = (byte)transform, Segment = (byte)segment, Skip = skip };
                ReadMotion(ref bits, row, column, ref motion, above, left, hasAbove, hasLeft);
                StoreBlock(ref bits, row, column, motion, 0); return;
            }
            uint modes = 0;
            if (size >= 3)
            {
                int mode = IntraMode(ref bits, size, hasAbove ? above.SubMode(2) : 0, hasLeft ? left.SubMode(1) : 0);
                modes = (uint)mode * 0x01010101u;
            }
            else
            {
                int width = Widths[size] / 4, height = Heights[size] / 4;
                for (int y = 0; y < 2; y += height)
                {
                    for (int x = 0; x < 2; x += width)
                    {
                        int upper = y > 0 ? (int)(modes >> (x * 8)) & 255 : hasAbove ? above.SubMode(2 + x) : 0;
                        int lower = x > 0 ? (int)(modes >> (y * 16)) & 255 : hasLeft ? left.SubMode(1 + y * 2) : 0;
                        int mode = IntraMode(ref bits, size, upper, lower);
                        for (int dy = 0; dy < height; dy++)
                        { for (int dx = 0; dx < width; dx++) { modes |= (uint)mode << (((y + dy) * 2 + x + dx) * 8); } }
                    }
                }
            }
            int lumaMode = (int)(modes >> 24), chromaMode = header.IsIntra ? bits.Tree(IntraTree, Vp9Tables.KeyChroma.Slice(lumaMode * 9, 9)) :
                symbols.Tree(ref bits, IntraTree, probabilities, Vp9ProbabilityOffset.Chroma + lumaMode * 9);
            Vp9BlockInfo block = new() { Size = (byte)size, Transform = (byte)transform, Segment = (byte)segment,
                Mode = (byte)lumaMode, SubModes = modes, Skip = skip };
            StoreBlock(ref bits, row, column, block, chromaMode);
        }

        private void StoreBlock(ref Vp9BooleanReader bits, int row, int column, Vp9BlockInfo block, int chromaMode)
        {
            Residual(ref bits, row, column, ref block, chromaMode);
            int size = block.Size;
            for (int y = 0; y < Math.Max(1, Heights[size] / 8); y++)
            { blocks.Slice((row + y) * miStride + column, Math.Max(1, Widths[size] / 8)).Fill(block); }
        }

        private int IntraMode(ref Vp9BooleanReader bits, int size, int above, int left) => header.IsIntra
            ? bits.Tree(IntraTree, Vp9Tables.KeyLuma.Slice((above * 10 + left) * 9, 9))
            : symbols.Tree(ref bits, IntraTree, probabilities, Vp9ProbabilityOffset.Luma + Math.Min(3, size / 3) * 9);

        private void Residual(ref Vp9BooleanReader bits, int row, int column, ref Vp9BlockInfo block, int chromaMode)
        {
            Span<int> coefficients = stackalloc int[1024]; int totalNonzero = 0; int depth = header.Geometry.BitDepth;
            for (int plane = 0; plane < 3; plane++)
            {
                int sx = plane == 0 ? 0 : subX, sy = plane == 0 ? 0 : subY, planeStride = stride >> sx;
                int planeOffset = plane switch { 0 => 0, 1 => yLength, 2 => yLength + uvLength, _ => throw new InvalidDataException("Invalid VP9 plane.") };
                Span<T> samples = pixels[planeOffset..];
                int width4 = Math.Max(8, (int)Widths[block.Size]) >> (2 + sx), height4 = Math.Max(8, (int)Heights[block.Size]) >> (2 + sy);
                int transform = plane == 0 ? block.Transform : Math.Min(block.Transform, Math.Min(BitOperations.Log2((uint)width4), BitOperations.Log2((uint)height4)));
                int step = 1 << transform, size = step * 4, baseX = column * 8 >> sx, baseY = row * 8 >> sy;
                int maxX = (header.MiColumns * 8 >> sx) - 1, maxY = (header.MiRows * 8 >> sy) - 1;
                int contextOffset = plane * miStride * 2, leftOffset = plane * 16, blockIndex = 0;
                if (block.Inter) { PredictInter(samples, planeStride, row, column, block, plane, baseX, baseY, width4, height4); }
                (int dc, int ac) = Vp9Quantizer.Get(header, state, block.Segment, plane);
                for (int y = 0; y < height4; y += step)
                {
                    for (int x = 0; x < width4; x += step, blockIndex++)
                    {
                        int startX = baseX + x * 4, startY = baseY + y * 4, x4 = startX / 4, y4 = startY / 4;
                        int end = 0;
                        if (startX <= maxX && startY <= maxY)
                        {
                            int mode = plane == 0 ? block.Size < 3 ? block.SubMode(blockIndex) : block.Mode : chromaMode;
                            if (!block.Inter) { Vp9IntraPrediction.Predict(samples, planeStride, maxX, maxY, startX, startY, size, mode,
                                column > tile.ColumnStart || x > 0, row > 0 || y > 0, x + step < width4, depth); }
                            if (!block.Skip)
                            {
                                int upper = 0, lower = 0;
                                for (int i = 0; i < step; i++)
                                {
                                    if (x4 + i <= maxX / 4) { upper |= aboveNonzero[contextOffset + x4 + i]; }
                                    if (y4 + i <= maxY / 4) { lower |= leftNonzero[leftOffset + ((y4 + i) & 15)]; }
                                }
                                Vp9TransformType type = plane != 0 || transform == 3 || header.Lossless || block.Inter ? Vp9TransformType.DctDct : TransformTypes[mode];
                                int probabilityContext = ((transform * 2 + (plane == 0 ? 0 : 1)) * 2 + (block.Inter ? 1 : 0)) * 36;
                                end = Vp9CoefficientReader.Read(ref bits, coefficients, transform, type, depth, probabilities.Coefficient, probabilityContext, upper + lower, counts);
                                if (end > 0)
                                {
                                    totalNonzero++; Dequantize(coefficients, transform, type, dc, ac, depth, end);
                                    Vp9Transform.Inverse(coefficients, transform + 2, type, header.Lossless);
                                    for (int iy = 0; iy < size; iy++)
                                    {
                                        Span<T> destination = samples.Slice((startY + iy) * planeStride + startX, size);
                                        for (int ix = 0; ix < size; ix++)
                                        { destination[ix] = T.CreateTruncating(Math.Clamp(int.CreateTruncating(destination[ix]) + coefficients[iy * size + ix], 0, (1 << depth) - 1)); }
                                    }
                                }
                            }
                        }
                        byte nonzero = end > 0 ? (byte)1 : (byte)0;
                        aboveNonzero.Slice(contextOffset + x4, step).Fill(nonzero);
                        for (int i = 0; i < step; i++) { leftNonzero[leftOffset + ((y4 + i) & 15)] = nonzero; }
                    }
                }
            }
            if (block.Inter && block.Size >= 3 && totalNonzero == 0) { block.Skip = true; }
        }

        private static void Dequantize(Span<int> coefficients, int transform, Vp9TransformType type, int dc, int ac, int depth, int end)
        {
            ReadOnlySpan<ushort> scan = Vp9Scan.Get(transform, type); int denominator = transform == 3 ? 2 : 1, limit = 1 << (depth + 7);
            for (int i = 0; i < end; i++)
            {
                int position = scan[i]; long value = (long)coefficients[position] * (position == 0 ? dc : ac) / denominator;
                if (value < -limit || value >= limit) { throw new InvalidDataException("A VP9 dequantized coefficient exceeds the permitted precision."); }
                coefficients[position] = (int)value;
            }
        }

        private static ReadOnlySpan<Vp9TransformType> TransformTypes =>
            [Vp9TransformType.DctDct, Vp9TransformType.AdstDct, Vp9TransformType.DctAdst, Vp9TransformType.DctDct,
             Vp9TransformType.AdstAdst, Vp9TransformType.AdstDct, Vp9TransformType.DctAdst, Vp9TransformType.DctAdst,
             Vp9TransformType.AdstDct, Vp9TransformType.AdstAdst];
    }
}
