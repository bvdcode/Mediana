// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcInterResidualTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        public void ResidualsModifyOnlyTheirTransformBlockAndPublishAvailability(int depth)
        {
            using SamplePlane plane = new(16, 16, depth);
            int maximum = (1 << depth) - 1;
            if (depth == 8) { plane.ByteView.Samples.Fill(128); }
            else { plane.WordView.Samples.Fill(128); }
            int[] residual = Enumerable.Range(0, 16).Select(i => (i - 8) * maximum).ToArray();
            Assert.Equal(-1, plane.Neighbour(4, 8));
            plane.AddResidual(4, 8, 4, residual);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int actual = depth == 8 ? plane.ReadByteView.Row(y)[x] : plane.ReadWordView.Row(y)[x];
                    bool inside = x is >= 4 and < 8 && y is >= 8 and < 12;
                    int expected = inside ? Math.Clamp(128 + residual[(y - 8) * 4 + x - 4], 0, maximum) : 128;
                    Assert.Equal(expected, actual);
                    Assert.Equal(inside ? expected : -1, plane.Neighbour(x, y));
                }
            }
            plane.Freeze();
            Assert.Throws<InvalidOperationException>(() => plane.AddResidual(4, 8, 4, residual));
            Assert.Throws<InvalidOperationException>(() => plane.MarkAvailable(4, 8, 4, 4));
        }

        [Fact]
        public void InterSkipAndResidualMetadataPreserveCodingDepthAndDistinguishPredictionEdges()
        {
            using CodingBlockMap blocks = new(32, 32);
            Assert.False(blocks.IsIntra(0, 0));
            Assert.False(blocks.IsSkipped(0, 0));
            blocks.SetCodingUnit(0, 0, 16, 1, false);
            Assert.True(blocks.IsIntra(0, 0));
            blocks.SetInter(0, 0, 16, true);
            blocks.MarkPrediction(0, 8, 16, 8);
            blocks.MarkLumaResidual(8, 8, 8);
            for (int y = 0; y < 16; y += 4)
            {
                for (int x = 0; x < 16; x += 4)
                {
                    Assert.Equal(1, blocks.Depth(x, y));
                    Assert.False(blocks.IsIntra(x, y));
                    Assert.True(blocks.IsSkipped(x, y));
                    Assert.Equal(x >= 8 && y >= 8, blocks.HasLumaResidual(x, y));
                }
            }
            Assert.True(blocks.Boundary(4, 8, false));
            Assert.False(blocks.TransformBoundary(4, 8, false));
            blocks.MarkTransform(0, 8, 8);
            Assert.True(blocks.TransformBoundary(4, 8, false));
            Assert.Equal(-1, blocks.Depth(20, 20));
        }
    }
}
