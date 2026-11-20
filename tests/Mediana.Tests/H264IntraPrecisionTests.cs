// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264IntraPrecisionTests
    {
        [Theory]
        [InlineData(1023)]
        [InlineData(4095)]
        public void DcAndPlaneUseTheDeclaredPrecision(int maximum)
        {
            ushort[] output = new ushort[256];
            H264IntraPrediction<ushort>.Luma16x16(2, [], [], null, output, maximum);
            Assert.All(output, sample => Assert.Equal((maximum + 1) / 2, sample));
            ushort[] neighbours = new ushort[16];
            neighbours.AsSpan(8).Fill((ushort)maximum);
            H264IntraPrediction<ushort>.Luma16x16(3, neighbours, neighbours, 0, output, maximum);
            Assert.Equal(0, output[0]);
            Assert.Equal(maximum, output[^1]);
            Assert.Contains(output, sample => sample > 255 && sample < maximum);
        }

        [Fact]
        public void EightByEightFilteringPreservesLowBitsAboveBytePrecision()
        {
            ushort[] output = new ushort[64];
            ushort[] top = Enumerable.Range(1000, 16).Select(value => (ushort)value).ToArray();
            H264IntraPrediction<ushort>.Luma8x8(0, top, [], 999, output, 1023);
            for (int row = 0; row < 8; row++)
            {
                Assert.Equal(top[..8], output.AsSpan(row * 8, 8).ToArray());
            }
        }

        [Fact]
        public void Chroma422UsesAllSixteenNeighbourRowsAtFullPrecision()
        {
            ushort[] output = new ushort[128];
            ushort[] left = Enumerable.Range(900, 16).Select(value => (ushort)value).ToArray();
            H264IntraPrediction<ushort>.Chroma(1, [], left, null, output, 16, 1023);
            for (int row = 0; row < 16; row++)
            {
                Assert.All(output.AsSpan(row * 8, 8).ToArray(), sample => Assert.Equal(left[row], sample));
            }
            H264IntraPrediction<ushort>.Chroma(0, [], [], null, output, 16, 1023);
            Assert.All(output, sample => Assert.Equal(512, sample));
        }
    }
}
