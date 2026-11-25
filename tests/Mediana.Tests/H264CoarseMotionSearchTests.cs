// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CoarseMotionSearchTests
    {
        [Theory]
        [InlineData(16, 16)]
        [InlineData(80, 48)]
        [InlineData(528, 912)]
        public void ReferenceAndMacroblockUseTheSameRoundedFourByFourMeans(int width, int height)
        {
            byte[] samples = new byte[width * height];
            new Random(27).NextBytes(samples);
            byte[] reduced = new byte[width / 4 * (height / 4)];
            H264QuarterLuma.Reduce(samples, width, height, reduced, default);
            byte[] block = new byte[256];
            for (int y = 0; y < height; y += 16)
            {
                for (int x = 0; x < width; x += 16)
                {
                    for (int row = 0; row < 16; row++)
                    {
                        samples.AsSpan((y + row) * width + x, 16).CopyTo(block.AsSpan(row * 16));
                    }
                    Vector128<byte> small = H264QuarterLuma.ReduceMacroblock(block);
                    for (int index = 0; index < 16; index++)
                    {
                        int bx = (index % 4) * 4;
                        int by = index / 4 * 4;
                        int sum = 0;
                        for (int row = 0; row < 4; row++)
                        {
                            for (int col = 0; col < 4; col++)
                            {
                                sum += block[(by + row) * 16 + bx + col];
                            }
                        }
                        byte expected = (byte)((sum + 8) / 16);
                        Assert.Equal(expected, reduced[(y + by) / 4 * (width / 4) + (x + bx) / 4]);
                        Assert.Equal(expected, small.GetElement(index));
                    }
                }
            }
        }

        [Fact]
        public void FindsDistantMatchingTextureStartingWithAStationaryPredictor()
        {
            const int stride = 40;
            byte[] plane = new byte[stride * 20];
            Vector128<byte> source = Vector128<byte>.Zero;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    byte value = (byte)(100 + row * 17 + column * 9);
                    plane[(8 + row) * stride + 16 + column] = value;
                    source = source.WithElement(row * 4 + column, value);
                }
            }
            H264CoarseMotionSearch search = new(plane, stride, 20, source, (8, 8), (0, 0));
            Assert.Equal((128, 0), search.Find(64));
            Assert.InRange(search.Find(16).X, -64, 64);
        }

        [Theory]
        [InlineData(8, 8, 5, -3)]
        [InlineData(0, 5, -2, 10)]
        [InlineData(24, 12, 2, 10)]
        public void ScoresThePredictorWithIndependentRowAndColumnBorderExtension(int x, int y, int dx, int dy)
        {
            const int width = 28;
            const int height = 24;
            byte[] plane = new byte[width * height];
            new Random(89).NextBytes(plane);
            Vector128<byte> block = Vector128<byte>.Zero;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    int offset = Math.Clamp(y + dy + row, 0, height - 1) * width + Math.Clamp(x + dx + column, 0, width - 1);
                    block = block.WithElement(row * 4 + column, plane[offset]);
                }
            }
            (int X, int Y) expected = (dx * 16, dy * 16);
            H264CoarseMotionSearch search = new(plane, width, height, block, (x, y), expected);
            Assert.Equal(expected, search.Find(64));
        }
    }
}
