// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1TilePrecisionTests
    {
        [Theory]
        [InlineData("tenFlat")]
        [InlineData("tenLossless")]
        [InlineData("tenQuantized")]
        [InlineData("tenPalette")]
        [InlineData("tenMatrix")]
        public void IntraTileMatchesIndependentMainTenPixels(string fixture) =>
            Av1TilePrecisionTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data"), fixture);

        [Theory]
        [InlineData(10)]
        [InlineData(12)]
        public void ByteStorageRejectsHighPrecisionBeforeWritingSamples(int depth)
        {
            byte[] output = Enumerable.Repeat((byte)123, 64).ToArray();
            Assert.Throws<ArgumentException>(() => InvalidStorage<byte>(output, depth));
            Assert.All(output, value => Assert.Equal((byte)123, value));
        }

        [Fact]
        public void NonNativeStorageTypeFailsBeforeChangingSamples()
        {
            uint[] output = Enumerable.Repeat(123u, 64).ToArray();
            Assert.Throws<ArgumentException>(() => InvalidStorage<uint>(output, 10));
            Assert.All(output, value => Assert.Equal(123u, value));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(16)]
        public void InvalidDepthFailsBeforeChangingSamples(int depth)
        {
            ushort[] output = Enumerable.Repeat((ushort)123, 64).ToArray();
            Assert.Throws<ArgumentException>(() => InvalidStorage<ushort>(output, depth));
            Assert.All(output, value => Assert.Equal((ushort)123, value));
        }

        private static void InvalidStorage<T>(T[] output, int depth) where T : unmanaged, System.Numerics.IBinaryInteger<T>
        {
            Av1SequenceHeader sequence = default(Av1SequenceHeader) with { Width = 8, Height = 8, BitDepth = depth };
            _ = new Av1TileDecoder<T>(default, sequence, default, output, default, default, default, default);
        }
    }
}
