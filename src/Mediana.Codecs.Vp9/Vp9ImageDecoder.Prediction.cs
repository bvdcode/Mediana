// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Vp9
{
    internal ref partial struct Vp9ImageDecoder<T> where T : unmanaged, IBinaryInteger<T>
    {
        private void PredictInter(Span<T> samples, int planeStride, int row, int column, Vp9BlockInfo block,
            int plane, int baseX, int baseY, int width4, int height4)
        {
            int sx = plane == 0 ? 0 : subX, sy = plane == 0 ? 0 : subY;
            int width = block.Size < 3 ? 1 : width4, height = block.Size < 3 ? 1 : height4;
            int blockIndex = 0;
            for (int y = 0; y < height4; y += height)
            {
                for (int x = 0; x < width4; x += width, blockIndex++)
                {
                    for (int list = 0; list < (block.ReferenceOne == 0 ? 1 : 2); list++)
                    {
                        Vp9Picture reference = (references ?? throw new InvalidDataException("VP9 inter prediction requires reference pictures.")).Get(header.ReferenceIndices[block.Reference(list) - 1]);
                        Vp9PictureGeometry geometry = reference.Geometry;
                        if (geometry.BitDepth != header.Geometry.BitDepth || geometry.SubsamplingX != header.Geometry.SubsamplingX || geometry.SubsamplingY != header.Geometry.SubsamplingY ||
                            2L * header.Geometry.Width < geometry.Width || 2L * header.Geometry.Height < geometry.Height || header.Geometry.Width > 16L * geometry.Width || header.Geometry.Height > 16L * geometry.Height)
                        { throw new InvalidDataException("Invalid VP9 prediction reference geometry."); }
                        Vp9MotionVector motion = SelectMotion(block, list, blockIndex, plane, sx, sy);
                        int bw = Math.Max(1, Widths[block.Size] / 8), bh = Math.Max(1, Heights[block.Size] / 8);
                        int borderX = (4 + ((bw * 8) >> sx)) * 16, borderY = (4 + ((bh * 8) >> sy)) * 16;
                        int mvX = Math.Clamp((motion.X * 2) >> sx, ((-column * 128) >> sx) - borderX, (((header.MiColumns - bw - column) * 128) >> sx) + borderX - 16);
                        int mvY = Math.Clamp((motion.Y * 2) >> sy, ((-row * 128) >> sy) - borderY, (((header.MiRows - bh - row) * 128) >> sy) + borderY - 16);
                        int scaleX = (int)(((long)geometry.Width << 14) / header.Geometry.Width), scaleY = (int)(((long)geometry.Height << 14) / header.Geometry.Height);
                        int px = baseX + x * 4, py = baseY + y * 4;
                        int startX = checked((int)(((long)px * scaleX >> 14) * 16 + ((long)mvX * scaleX >> 14) + (((16L * (px << sx) * scaleX) >> 14) & 15)));
                        int startY = checked((int)(((long)py * scaleY >> 14) * 16 + ((long)mvY * scaleY >> 14) + (((16L * (py << sy) * scaleY) >> 14) & 15)));
                        int refYLength = reference.Stride * reference.PaddedHeight, refUvLength = refYLength >> (subX + subY);
                        int offset = plane switch { 0 => 0, 1 => refYLength, 2 => refYLength + refUvLength, _ => throw new InvalidDataException("Invalid VP9 reference plane.") };
                        ReadOnlySpan<T> source = MemoryMarshal.Cast<byte, T>(reference.Pixels.Memory.Span)[offset..];
                        Vp9InterPrediction.Predict(source, reference.Stride >> sx, (geometry.Width + sx) >> sx, (geometry.Height + sy) >> sy,
                            samples[(py * planeStride + px)..], planeStride, width * 4, height * 4, startX, startY,
                            (16 * scaleX) >> 14, (16 * scaleY) >> 14, block.Interpolation, header.Geometry.BitDepth, predictionWorkspace, list == 1);
                    }
                }
            }
        }

        private static Vp9MotionVector SelectMotion(Vp9BlockInfo block, int list, int index, int plane, int sx, int sy)
        {
            Vp9MotionVector first = block.Vector(list, index);
            if (plane == 0 || block.Size >= 3 || sx == 0 && sy == 0) { return first; }
            if (sx == 1 && sy == 1)
            {
                Vp9MotionVector sum = block.Vector(list, 0).Add(block.Vector(list, 1)).Add(block.Vector(list, 2)).Add(block.Vector(list, 3));
                return new(Round(sum.X, 4), Round(sum.Y, 4));
            }
            Vp9MotionVector pair = first.Add(block.Vector(list, index + (sx == 1 ? 1 : 2)));
            return new(Round(pair.X, 2), Round(pair.Y, 2));
        }
        private static int Round(int value, int divisor) => (value + (value < 0 ? -divisor / 2 : divisor / 2)) / divisor;
    }
}
