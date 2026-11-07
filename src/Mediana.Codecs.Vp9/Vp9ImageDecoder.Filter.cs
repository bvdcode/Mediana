// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal ref partial struct Vp9ImageDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        internal void Filter()
        {
            if (header.FilterLevel == 0) { return; }
            Span<int> levels = stackalloc int[8 * 8];
            for (int segment = 0; segment < 8; segment++)
            {
                int level = header.FilterLevel;
                if (header.Segmentation && state.SegmentEnabled[segment * 4 + 1])
                { level = Math.Clamp(state.AbsoluteSegments ? state.SegmentFeatures[segment * 4 + 1] : level + state.SegmentFeatures[segment * 4 + 1], 0, 63); }
                for (int reference = 0; reference < 4; reference++)
                {
                    for (int mode = 0; mode < 2; mode++)
                    {
                        int delta = reference == 0 ? state.ReferenceDeltas[0] : state.ReferenceDeltas[reference] + state.ModeDeltas[mode];
                        levels[segment * 8 + reference * 2 + mode] = header.FilterDeltas ? Math.Clamp(level + (delta << (header.FilterLevel >> 5)), 0, 63) : level;
                    }
                }
            }
            for (int row = 0; row < header.MiRows; row += 8)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int column = 0; column < header.MiColumns; column += 8)
                {
                    for (int plane = 0; plane < 3; plane++)
                    {
                        int sx = plane == 0 ? 0 : subX, sy = plane == 0 ? 0 : subY, planeStride = stride >> sx;
                        int offset = plane switch { 0 => 0, 1 => yLength, 2 => yLength + uvLength, _ => throw new InvalidDataException("Invalid VP9 filter plane.") };
                        Span<T> samples = pixels[offset..];
                        for (int pass = 0; pass < 2; pass++)
                        {
                            int sub = pass == 0 ? sx : sy, length = 64 >> (pass == 0 ? sy : sx);
                            for (int edge = 0; edge < (16 >> sub); edge++)
                            {
                                for (int i = 0; i < length; i++)
                                {
                                    int x = column * 8 + ((pass == 0 ? edge * 4 : i) << sx);
                                    int y = row * 8 + ((pass == 0 ? i : edge * 4) << sy);
                                    if (x >= header.MiColumns * 8 || y >= header.MiRows * 8 || (pass == 0 ? x == 0 : y == 0)) { continue; }
                                    int loopColumn = ((x >> 3) >> sx) << sx, loopRow = ((y >> 3) >> sy) << sy;
                                    Vp9BlockInfo block = blocks[loopRow * miStride + loopColumn];
                                    int level = levels[block.Segment * 8 + block.ReferenceZero * 2 + (block.Mode is 10 or 11 or 13 ? 1 : 0)]; if (level == 0) { continue; }
                                    int transform = block.Transform;
                                    if (plane > 0)
                                    {
                                        transform = block.Size < 3 ? 0 : Math.Min(transform,
                                            Math.Min(BitOperations.Log2((uint)(Widths[block.Size] >> sx)), BitOperations.Log2((uint)(Heights[block.Size] >> sy))) - 2);
                                    }
                                    int size = sub == 0 ? block.Size : Math.Max(6, (int)block.Size);
                                    int predictionExtent = pass == 0 ? Math.Max(8, (int)Widths[size]) : Math.Max(8, (int)Heights[size]);
                                    bool blockEdge = (pass == 0 ? x : y) % predictionExtent == 0;
                                    bool transformEdge = (!block.Inter || !block.Skip) && (edge & ((1 << transform) - 1)) == 0;
                                    if (pass == 1 && sx == 1 && (header.MiColumns & 1) != 0 && (edge & 1) != 0 && x + 8 >= header.MiColumns * 8)
                                    { transformEdge = false; }
                                    if (!blockEdge && !transformEdge) { continue; }
                                    int filterSize = transform == 0 && (edge & 7) == 0 ? 1 : Math.Min(2, transform);
                                    if (filterSize == 2 && (pass == 0 && sx == 1 && (x >> 3) == header.MiColumns - 1 ||
                                        pass == 1 && sy == 1 && (y >> 3) == header.MiRows - 1)) { filterSize = 1; }
                                    int sharpness = header.FilterSharpness, shift = sharpness > 4 ? 2 : sharpness > 0 ? 1 : 0;
                                    int limit = sharpness > 0 ? Math.Clamp(level >> shift, 1, 9 - sharpness) : Math.Max(1, level);
                                    Vp9LoopFilter.Apply(samples, (y >> sy) * planeStride + (x >> sx), pass == 0 ? 1 : planeStride,
                                        filterSize, level, limit, header.Geometry.BitDepth);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
