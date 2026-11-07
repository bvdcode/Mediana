// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class PlanarPreparationTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(16)]
        public void SamplingOrientationAndPrecisionMatchContinuousCoordinates(int depth)
        {
            foreach ((int horizontal, int vertical) in new[] { (1, 1), (1, 2), (2, 1), (2, 2) })
            {
                foreach ((int a, int b, int c, int d) in new[] { (1, 0, 0, 1), (-1, 0, 0, 1), (1, 0, 0, -1), (-1, 0, 0, -1),
                    (0, 1, 1, 0), (0, 1, -1, 0), (0, -1, 1, 0), (0, -1, -1, 0) })
                {
                    MediaBufferAllocator allocator = new(65536);
                    PlanarVideoPixelFormat pixels = new("planar", PlanarColorModel.Yuv, depth, horizontal, vertical);
                    VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Center);
                    using (VideoFrame input = PlanarSrgbTests.Create(new(7, 5, pixels, color), allocator,
                        (plane, x, y) => (plane * 31 + x * 13 + y * 19) << (depth - 8)))
                    {
                        VideoDisplayMatrix matrix = new(a, b, 0, c, d, 0, 0, 0, 1);
                        Yuv420FrameTransform operation = new(input.Format, matrix, VideoPixelFormat.Yuv420P8, allocator);
                        using VideoFrame output = operation.Apply(input);
                        decimal minimumX = Math.Min(0, 6 * a) + Math.Min(0, 4 * c), minimumY = Math.Min(0, 6 * b) + Math.Min(0, 4 * d);
                        for (int plane = 0; plane < 3; plane++)
                        {
                            for (int y = 0; y < output.Planes[plane].Height; y++)
                            {
                                ReadOnlySpan<byte> row = output.GetRow(plane, y);
                                for (int x = 0; x < row.Length; x++)
                                {
                                    decimal displayX = (plane == 0 ? x : 2 * x + 0.5m) + minimumX, displayY = (plane == 0 ? y : 2 * y + 0.5m) + minimumY;
                                    decimal sourceX = displayX * a + displayY * b, sourceY = displayX * c + displayY * d;
                                    decimal value;
                                    if (plane == 0) { value = Read(input, plane, (int)sourceX, (int)sourceY, depth); }
                                    else
                                    {
                                        value = Interpolate(input, plane, (sourceX - (horizontal == 1 ? 0 : 0.5m)) / horizontal,
                                        (sourceY - (vertical == 1 ? 0 : 0.5m)) / vertical, depth);
                                    }
                                    int expected = (int)decimal.Round(value / (1 << (depth - 8)), MidpointRounding.AwayFromZero);
                                    Assert.Equal(Math.Min(255, expected), row[x]);
                                }
                            }
                        }
                    }
                    Assert.Equal(0, allocator.AllocatedBytes);
                }
            }
        }

        [Theory]
        [InlineData(10, 8)]
        [InlineData(12, 8)]
        [InlineData(12, 10)]
        [InlineData(16, 8)]
        [InlineData(16, 10)]
        public void PrecisionReductionMatchesEveryInputCode(int sourceDepth, int targetDepth)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024); int width = 1 << sourceDepth;
            PlanarVideoPixelFormat source = new("source", PlanarColorModel.Yuv, sourceDepth, 2, 2), target = new("target", PlanarColorModel.Yuv, targetDepth, 2, 2);
            using VideoFrame input = PlanarSrgbTests.Create(new(width, 1, source), allocator, (_, x, _) => x);
            Yuv420FrameTransform operation = new(input.Format, VideoDisplayMatrix.Identity, target, allocator);
            using VideoFrame output = operation.Apply(input); int shift = sourceDepth - targetDepth;
            for (int x = 0; x < width; x++)
            { Assert.Equal(Math.Min((1 << targetDepth) - 1, (x + (1 << (shift - 1))) >> shift), Read(output, 0, x, 0, targetDepth)); }
        }

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void PlanarRgbUsesSharedVideoPreparationWithoutRgbaIntermediate(int depth)
        {
            MediaBufferAllocator allocator = new(4096); PlanarVideoPixelFormat pixels = new("gbr", PlanarColorModel.Gbr, depth);
            using VideoFrame input = PlanarSrgbTests.Create(new(3, 3, pixels, VideoColorInfo.Srgb), allocator, (plane, x, _) => plane == x ? (1 << depth) - 1 : 0);
            using VideoPreparation preparation = new(input.Format, VideoDisplayMatrix.Identity, allocator, new() { RgbConversion = new() });
            using VideoFrame output = preparation.Apply(input);
            Assert.Same(VideoPixelFormat.Yuv420P8, output.Format.PixelFormat);
            Assert.Equal(TransferFunction.Srgb, output.Format.Color.Transfer);
            ReadOnlySpan<byte> row = output.GetRow(0, 0);
            Assert.InRange(row[0], 172, 173); Assert.InRange(row[1], 31, 32); Assert.InRange(row[2], 62, 63);
        }

        private static decimal Interpolate(VideoFrame input, int plane, decimal x, decimal y, int depth)
        {
            int left = (int)decimal.Floor(x), top = (int)decimal.Floor(y); decimal dx = x - left, dy = y - top;
            return Read(input, plane, left, top, depth) * (1 - dx) * (1 - dy) + Read(input, plane, left + 1, top, depth) * dx * (1 - dy)
                + Read(input, plane, left, top + 1, depth) * (1 - dx) * dy + Read(input, plane, left + 1, top + 1, depth) * dx * dy;
        }
        private static int Read(VideoFrame input, int plane, int x, int y, int depth)
        {
            x = Math.Clamp(x, 0, input.Planes[plane].RowBytes / (depth == 8 ? 1 : 2) - 1); y = Math.Clamp(y, 0, input.Planes[plane].Height - 1);
            ReadOnlySpan<byte> row = input.GetRow(plane, y); return depth == 8 ? row[x] : MemoryMarshal.Cast<byte, ushort>(row)[x];
        }
    }
}
