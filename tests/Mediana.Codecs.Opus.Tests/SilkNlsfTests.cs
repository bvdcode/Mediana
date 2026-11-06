// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class SilkNlsfTests
    {
        [Fact]
        public void SyntaxPredictorsDequantizationAndStabilizationMatchIndependentDecoding() =>
            Assert.Equal(5888, OpusSilkNlsfTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(10)]
        [InlineData(16)]
        public void RepeatedLsfReconstructionAllocatesNoManagedStorage(int order)
        {
            SilkNlsfCodebook book = new(order);
            Span<sbyte> indices = stackalloc sbyte[17];
            Span<short> frequencies = stackalloc short[16];
            ReadOnlySpan<byte> bytes = [45, 14, 129, 255, 193, 65, 100, 29];
            for (int at = 0; at < 2; at++) { Decode(bytes, book, indices, frequencies); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int at = 0; at < 1000; at++) { Decode(bytes, book, indices, frequencies); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void InvalidResidualIndicesPreserveTheDestination()
        {
            sbyte[] indices = new sbyte[17]; indices[16] = 11;
            short[] frequencies = Enumerable.Repeat((short)17, 16).ToArray();
            Assert.Throws<ArgumentOutOfRangeException>(() => SilkNlsfReader.Dequantize(new(16), indices, frequencies));
            Assert.All(frequencies, value => Assert.Equal(17, value));
        }

        private static void Decode(ReadOnlySpan<byte> bytes, SilkNlsfCodebook book, Span<sbyte> indices, Span<short> frequencies)
        {
            OpusRangeDecoder decoder = new(bytes);
            SilkNlsfReader.Read(ref decoder, book, SilkSignalType.Voiced, indices);
            SilkNlsfReader.ReadInterpolation(ref decoder, 4);
            SilkNlsfReader.Dequantize(book, indices, frequencies);
        }
    }
}
