// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1WarpPrediction
    {
        internal static void Apply(Span<byte> output, Av1SequenceHeader target, Av1StoredPicture reference,
            int plane, int x, int y, int width, int height, Av1WarpParameters warp)
            => Predict(new(output[target.PlaneOffset(plane)..], target.PlaneStride(plane)), target, reference, plane, x, y, width, height, warp, default);

        internal static void Predict(Av1PredictionDestination destination, Av1SequenceHeader target, Av1StoredPicture reference,
            int plane, int x, int y, int width, int height, Av1WarpParameters warp, Av1CompoundBlend blend)
        {
            int sub = target.PlaneShift(plane);
            Av1SequenceHeader source = reference.Header.Geometry;
            int lastX = ((source.Width + sub) >> sub) - 1;
            int lastY = ((source.Height + sub) >> sub) - 1;
            int sourceStride = source.PlaneStride(plane);
            ReadOnlySpan<byte> pixels = reference.Pixels.Memory.Span[source.PlaneOffset(plane)..];
            Span<int> intermediate = stackalloc int[15 * 8];
            for (int blockY = 0; blockY < height; blockY += 8)
            {
                for (int blockX = 0; blockX < width; blockX += 8)
                {
                    int srcX = (x + blockX + 4) << sub;
                    int srcY = (y + blockY + 4) << sub;
                    long positionX = ((long)warp.A * srcX + (long)warp.B * srcY + warp.X) >> sub;
                    long positionY = ((long)warp.C * srcX + (long)warp.D * srcY + warp.Y) >> sub;
                    int ix = (int)(positionX >> 16);
                    int iy = (int)(positionY >> 16);
                    int fractionX = (int)positionX & 65472;
                    int fractionY = (int)positionY & 65472;
                    for (int row = -7; row < 8; row++)
                    {
                        int sourceRow = Math.Clamp(iy + row, 0, lastY) * sourceStride;
                        for (int column = -4; column < 4; column++)
                        {
                            int phase = ((fractionX + warp.Alpha * column + warp.Beta * row + 512) >> 10) + 64;
                            ReadOnlySpan<short> filter = Av1WarpTables.Filters.Slice(phase * 8, 8);
                            int sum = 0;
                            for (int tap = 0; tap < 8; tap++)
                            {
                                sum += filter[tap] * pixels[sourceRow + Math.Clamp(ix + column - 3 + tap, 0, lastX)];
                            }
                            intermediate[(row + 7) * 8 + column + 4] = (sum + 4) >> 3;
                        }
                    }
                    for (int row = 0; row < Math.Min(8, height - blockY); row++)
                    {
                        Span<byte> line = destination.Row(x + blockX, y + blockY + row, Math.Min(8, width - blockX));
                        for (int column = 0; column < line.Length; column++)
                        {
                            int phase = ((fractionY + warp.Gamma * (column - 4) + warp.Delta * (row - 4) + 512) >> 10) + 64;
                            ReadOnlySpan<short> filter = Av1WarpTables.Filters.Slice(phase * 8, 8);
                            int sum = 0;
                            for (int tap = 0; tap < 8; tap++) { sum += filter[tap] * intermediate[(row + tap) * 8 + column]; }
                            if (!blend.Active) { line[column] = (byte)Math.Clamp((sum + 1024) >> 11, 0, 255); }
                            else
                            {
                                byte color = blend.Apply(blockY + row, blockX + column, width, (sum + 64) >> 7);
                                if (blend.Second) { line[column] = color; }
                            }
                        }
                    }
                }
            }
        }
    }
}
