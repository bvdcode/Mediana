// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1RestorationPrecisionTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void WienerAndSelfGuidedSamplesMatchIndependentHighDepthKernels(int bitDepth) =>
            Av1FilterPrecisionTestInput.VerifyRestoration(Path.Combine(AppContext.BaseDirectory, "Data"), bitDepth);

        [Fact]
        public void ConstantTwelveBitFieldDoesNotOverflowTheSelfGuidedMean()
        {
            ushort[] rows = Enumerable.Repeat((ushort)4095, 80).ToArray();
            ushort[] output = new ushort[64];
            Av1RestorationSource<ushort> input = new(rows, default, 8, 8, 8, 0, 0, 64);
            Av1RestorationSelfGuided.Filter(input, output, 8, 0, 0, 4, 4, new() { Set = 0, X0 = -96, X1 = -32 }, 12);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal((ushort)(x < 4 && y < 4 ? 4093 : 0), output[y * 8 + x]);
                }
            }
        }

        [Fact]
        public void HighDepthSourceKeepsTheOriginalStripeBoundarySamples()
        {
            ushort[] rows = Enumerable.Range(256, 40).Select(value => (ushort)value).ToArray();
            ushort[] boundaries = Enumerable.Range(512, 32).Select(value => (ushort)value).ToArray();
            Av1RestorationSource<ushort> input = new(rows, boundaries, 4, 4, 128, 56, 1, 64);
            Assert.Equal(512, input.Sample(-1, 53));
            Assert.Equal(515, input.Sample(5, 54));
            Assert.Equal(517, input.Sample(1, 55));
            Assert.Equal(269, input.Sample(1, 56));
            Assert.Equal(538, input.Sample(2, 120));
            Assert.Equal(541, input.Sample(1, 122));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(16)]
        public void InvalidDepthFailsBeforeWritingOutput(int bitDepth)
        {
            ushort[] output = Enumerable.Repeat((ushort)123, 64).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => InvalidWiener(output, bitDepth));
            Assert.Throws<ArgumentOutOfRangeException>(() => InvalidSelfGuided(output, bitDepth));
            Assert.All(output, value => Assert.Equal((ushort)123, value));
        }

        private static void InvalidWiener(ushort[] output, int bitDepth)
        {
            Av1RestorationSource<ushort> input = new(default, default, 8, 8, 8, 0, 0, 64);
            Av1RestorationWiener.Filter(input, output, 8, 0, 0, 4, 4, default, bitDepth);
        }

        private static void InvalidSelfGuided(ushort[] output, int bitDepth)
        {
            Av1RestorationSource<ushort> input = new(default, default, 8, 8, 8, 0, 0, 64);
            Av1RestorationSelfGuided.Filter(input, output, 8, 0, 0, 4, 4, default, bitDepth);
        }
    }
}
