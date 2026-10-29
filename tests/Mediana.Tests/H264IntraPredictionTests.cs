// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264IntraPredictionTests
    {
        [Theory]
        [InlineData(0, new byte[] { 10, 20, 30, 40 }, new byte[] { 10, 20, 30, 40 })]
        [InlineData(1, new byte[] { 90, 90, 90, 90 }, new byte[] { 120, 120, 120, 120 })]
        [InlineData(2, new byte[] { 65, 65, 65, 65 }, new byte[] { 65, 65, 65, 65 })]
        [InlineData(3, new byte[] { 20, 30, 40, 50 }, new byte[] { 50, 60, 70, 78 })]
        [InlineData(4, new byte[] { 50, 23, 20, 30 }, new byte[] { 110, 100, 83, 50 })]
        [InlineData(5, new byte[] { 30, 15, 25, 35 }, new byte[] { 100, 50, 23, 20 })]
        [InlineData(6, new byte[] { 70, 50, 23, 20 }, new byte[] { 115, 110, 105, 100 })]
        [InlineData(7, new byte[] { 15, 25, 35, 45 }, new byte[] { 30, 40, 50, 60 })]
        [InlineData(8, new byte[] { 95, 100, 105, 110 }, new byte[] { 120, 120, 120, 120 })]
        public void DirectionalModesMatchNormativePredictionVectors(int mode, byte[] firstRow, byte[] lastRow)
        {
            byte[] output = new byte[16];
            H264IntraPrediction.Luma4x4(mode, [10, 20, 30, 40, 50, 60, 70, 80], [90, 100, 110, 120], 50, output);
            Assert.Equal(firstRow, output[..4]);
            Assert.Equal(lastRow, output[12..]);
        }

        [Fact]
        public void ChromaDcUsesDifferentNeighboursInItsFourQuadrants()
        {
            byte[] output = new byte[64];
            H264IntraPrediction.Chroma8x8(0, [10, 10, 10, 10, 30, 30, 30, 30], [50, 50, 50, 50, 90, 90, 90, 90], null, output);
            Assert.Equal(new byte[] { 30, 30, 30, 30, 30, 30, 30, 30 }, output[..8]);
            Assert.Equal(new byte[] { 90, 90, 90, 90, 60, 60, 60, 60 }, output[56..]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void DirectionalModesRejectUnavailableNeighbours(int mode) =>
            Assert.Throws<InvalidDataException>(() => H264IntraPrediction.Luma4x4(mode, [], [], null, new byte[16]));

        [Fact]
        public void DcAndPlanePredictionHandleBordersRoundingAndClipping()
        {
            byte[] output = new byte[256];
            H264IntraPrediction.Luma16x16(2, [], [], null, output);
            Assert.All(output, sample => Assert.Equal(128, sample));
            H264IntraPrediction.Luma4x4(2, [10, 20, 30, 40], [], null, output);
            Assert.All(output[..16], sample => Assert.Equal(25, sample));
            H264IntraPrediction.Luma16x16(3,
                [0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255],
                [0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255], 0, output);
            Assert.Equal(0, output[0]);
            Assert.Equal(255, output[255]);
        }
    }
}
