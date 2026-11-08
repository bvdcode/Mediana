// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1FilterPrecisionTests
    {
        private static string Data => Path.Combine(AppContext.BaseDirectory, "Data");

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void DirectionVarianceAndCdefSamplesMatchIndependentHighDepthKernels(int bitDepth) =>
            Av1FilterPrecisionTestInput.VerifyCdef(Data, bitDepth);

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void LumaVarianceAdjustsScaledCdefStrengthBeforeFiltering(int bitDepth) =>
            Av1FilterPrecisionTestInput.VerifyCdefVariance(Data, bitDepth);

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void EveryDeblockingExtentAndDirectionMatchesIndependentHighDepthKernels(int bitDepth) =>
            Av1FilterPrecisionTestInput.VerifyDeblocking(Data, bitDepth);

        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        public void ByteDeblockingRejectsUnrepresentableSampleDepth(int bitDepth)
        {
            byte[] output = Enumerable.Repeat((byte)123, 256).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1LoopFilterKernel.Apply<byte>(output, 136, 1, 8, false, 63, 193, 2, bitDepth));
            Assert.All(output, value => Assert.Equal((byte)123, value));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(16)]
        public void InvalidDepthFailsBeforeChangingPixels(int bitDepth)
        {
            ushort[] source = new ushort[256];
            ushort[] output = Enumerable.Repeat((ushort)123, 256).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1CdefDirection.Find<ushort>(source, 16, out _, bitDepth));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1CdefKernel.Filter<ushort>(source, output, 16, 16, 4, 4, 8, 3, 2, 4, 0, bitDepth));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1LoopFilterKernel.Apply<ushort>(output, 136, 1, 8, false, 63, 193, 2, bitDepth));
            Assert.All(output, value => Assert.Equal((ushort)123, value));
        }
    }
}
