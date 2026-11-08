// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1QuantizationPrecisionTests
    {
        [Fact]
        public void EverySampleDepthQuantizerMatchesIndependentTableHashes()
        {
            using BinaryReader reader = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "quantizers.sha256")));
            Assert.Equal("AV1QLUT1"u8.ToArray(), reader.ReadBytes(8));
            foreach (int bitDepth in new[] { 8, 10, 12 })
            {
                Assert.Equal(reader.ReadBytes(32), Digest(Av1QuantizerTables.DcForDepth(bitDepth)));
                Assert.Equal(reader.ReadBytes(32), Digest(Av1QuantizerTables.AcForDepth(bitDepth)));
            }
            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        }

        [Theory]
        [InlineData(8, 1336, 1828)]
        [InlineData(10, 5347, 7312)]
        [InlineData(12, 21387, 29247)]
        public void PlaneDeltasClampQuantizerIndicesBeforeDepthLookup(int depth, int dcMaximum, int acMaximum)
        {
            Av1Quantization quantization = new(255, 10, -255, -255, -255, -255);
            Assert.Equal(dcMaximum, quantization.Dc(0, depth));
            Assert.Equal(acMaximum, quantization.Ac(0, depth));
            Assert.Equal(4, quantization.Dc(1, depth));
            Assert.Equal(4, quantization.Dc(2, depth));
            Assert.Equal(4, quantization.Ac(1, depth));
            Assert.Equal(4, quantization.Ac(2, depth));
        }

        [Theory]
        [InlineData(8, 32767, -32768)]
        [InlineData(10, 131071, -131072)]
        [InlineData(12, 524287, -524288)]
        public void DequantizationRetainsTheDeclaredSignedPrecision(int depth, int maximum, int minimum)
        {
            Assert.Equal(maximum, Av1Quantization.Dequantize(1048575, 13825, false, 0, depth));
            Assert.Equal(minimum, Av1Quantization.Dequantize(1048575, 13825, true, 0, depth));
            Assert.Equal(1, Av1Quantization.Dequantize(571233, 11425, false, 0, depth));
            Assert.Equal(-1, Av1Quantization.Dequantize(571233, 11425, true, 0, depth));
            Assert.Equal(0, Av1Quantization.Dequantize(571233, 11425, true, 2, depth));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        [InlineData(16)]
        public void InvalidDepthCannotSelectAnotherPrecision(int depth)
        {
            Av1Quantization quantization = new(0, 0, 0, 0, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => quantization.Dc(0, depth));
            Assert.Throws<ArgumentOutOfRangeException>(() => quantization.Ac(0, depth));
            Assert.Throws<ArgumentOutOfRangeException>(() => Av1Quantization.Dequantize(0, 0, false, 0, depth));
        }

        private static byte[] Digest(ReadOnlySpan<ushort> values)
        {
            byte[] encoded = new byte[values.Length * 2];
            for (int index = 0; index < values.Length; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(index * 2), values[index]);
            }
            return SHA256.HashData(encoded);
        }
    }
}
