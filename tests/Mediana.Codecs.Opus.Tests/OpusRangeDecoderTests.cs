// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Opus;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusRangeDecoderTests
    {
        [Fact]
        public void EverySymbolRangeAndBitBudgetMatchesIndependentReference() =>
            Assert.Equal(65071, OpusRangeTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Fact]
        public void RawBitsReadBackwardsAcrossBytesAndPermitRangeLookaheadOverlap()
        {
            OpusRangeDecoder decoder = new(new byte[] { 0x81, 0x23, 0x45, 0x67 });
            Assert.Equal(1, decoder.BitsUsed);
            Assert.Equal(7u, decoder.ReadRawBits(4));
            Assert.Equal(0x56u, decoder.ReadRawBits(8));
            Assert.Equal(0x234u, decoder.ReadRawBits(12));
            Assert.Equal(0x81u, decoder.ReadRawBits(8));
            Assert.Equal(33, decoder.BitsUsed);
        }

        [Fact]
        public void RangeZeroExtensionContinuesAfterTheLastByteAndNeedsNoAllocations()
        {
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            Assert.Equal(0x80000000u, decoder.Range);
            Assert.Equal(1, decoder.BitsUsed);
            Assert.Equal(8, decoder.EighthBitsUsed);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool one = false;
            uint total = 0;
            for (int index = 0; index < 10000; index++)
            {
                one |= decoder.ReadBit(1);
                total |= decoder.ReadUniform(256);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(one);
            Assert.Equal(0u, total);
            Assert.Equal(90001, decoder.BitsUsed);
            Assert.Equal(0, allocated);
        }

        [Fact]
        public void CorruptUniformValuesAndRawOverreadAreRejected()
        {
            Assert.Throws<InvalidDataException>(ReadCorruptUniform);
            Assert.Throws<InvalidDataException>(ReadPastRawEnd);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void InvalidEntropyModelsAndWidthsAreRejected(int kind) =>
            Assert.ThrowsAny<ArgumentException>(() => ReadInvalid(kind));

        private static void ReadInvalid(int kind)
        {
            OpusRangeDecoder decoder = new(ReadOnlySpan<byte>.Empty);
            switch (kind)
            {
                case 0: decoder.ReadBit(0); break;
                case 1: decoder.ReadBit(32); break;
                case 2: decoder.ReadInverseCdf([]); break;
                case 3: decoder.ReadInverseCdf([2, 0], 1); break;
                case 4: decoder.ReadFrequencies([65535, 1]); break;
                case 5: decoder.ReadUniform(0); break;
                case 6: decoder.ReadRawBits(33); break;
                default: throw new InvalidOperationException("An invalid entropy case is absent.");
            }
        }

        private static void ReadCorruptUniform()
        {
            OpusRangeDecoder decoder = new(new byte[] { 255, 255, 255 });
            decoder.ReadUniform(257);
        }

        private static void ReadPastRawEnd()
        {
            OpusRangeDecoder decoder = new(new byte[] { 0x7f });
            decoder.ReadRawBits(7);
            decoder.ReadRawBits(2);
        }

    }
}
