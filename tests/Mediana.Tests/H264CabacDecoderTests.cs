// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264CabacDecoderTests
    {
        [Fact]
        public void MixedBinsMatchTheExactIntervalWriter() => H264CabacTestInput.VerifyMixedBins();

        [Fact]
        public void MostProbableDecisionAdvancesStateWithoutRenormalizing()
        {
            H264BitReader reader = new([0, 0]);
            H264CabacDecoder decoder = new(ref reader);
            H264CabacContext context = new(0, false);
            Assert.False(decoder.ReadBin(ref reader, ref context));
            Assert.Equal(1, context.State);
            Assert.Equal(270, decoder.Range);
            Assert.Equal(0, decoder.Offset);
        }

        [Fact]
        public void LeastProbableDecisionAtStateZeroFlipsTheSymbolAndRenormalizes()
        {
            H264BitReader reader = new([0xfe, 0x80]);
            H264CabacDecoder decoder = new(ref reader);
            H264CabacContext context = new(0, false);
            Assert.True(decoder.ReadBin(ref reader, ref context));
            Assert.Equal(0, context.State);
            Assert.True(context.MostProbableSymbol);
            Assert.Equal(480, decoder.Range);
            Assert.Equal(478, decoder.Offset);
        }

        [Fact]
        public void BypassPreservesTheRange()
        {
            H264BitReader reader = new([0x96, 0]);
            H264CabacDecoder decoder = new(ref reader);
            Assert.True(decoder.ReadBypass(ref reader));
            Assert.Equal(510, decoder.Range);
            Assert.Equal(90, decoder.Offset);
        }

        [Fact]
        public void FalseTerminationAllowsAnotherDecision()
        {
            H264BitReader reader = new([0, 0]);
            H264CabacDecoder decoder = new(ref reader);
            Assert.False(decoder.ReadTermination(ref reader));
            Assert.Equal(508, decoder.Range);
            H264CabacContext context = new(0, true);
            Assert.True(decoder.ReadBin(ref reader, ref context));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void TrueTerminationRejectsEveryFurtherRead(int operation) =>
            Assert.Throws<InvalidOperationException>(() => ReadAfterTermination(operation));

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0 })]
        [InlineData(new byte[] { 255, 0 })]
        [InlineData(new byte[] { 255, 128 })]
        public void RejectsTruncationAndForbiddenInitialOffsets(byte[] payload) =>
            Assert.Throws<InvalidDataException>(() => Initialize(payload));

        [Fact]
        public void RejectsAnUnalignedStart() =>
            Assert.Throws<InvalidDataException>(() => Initialize([0, 0], 1));

        [Fact]
        public void RejectsExhaustedRenormalization() =>
            Assert.Throws<InvalidDataException>(ReadPastEnd);

        [Theory]
        [InlineData(-1)]
        [InlineData(33)]
        [InlineData(int.MaxValue)]
        public void RejectsInvalidBypassWidths(int count) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadBypassWidth(count));

        [Fact]
        public void ReadsThroughH264EmulationPreventionWithoutReplacingTheReader()
        {
            H264BitReader reader = new([0, 0, 3, 0, 0, 3, 0]);
            H264CabacDecoder decoder = new(ref reader);
            for (int index = 0; index < 31; index++)
            {
                Assert.False(decoder.ReadBypass(ref reader));
            }
            Assert.True(reader.ByteAligned);
            Assert.Equal(510, decoder.Range);
            Assert.Equal(0, decoder.Offset);
        }

        private static void Initialize(byte[] payload, int prefixBits = 0)
        {
            H264BitReader reader = new(payload);
            reader.ReadBits(prefixBits);
            _ = new H264CabacDecoder(ref reader);
        }

        private static void ReadAfterTermination(int operation)
        {
            H264BitReader reader = new([0xfe, 0x80]);
            H264CabacDecoder decoder = new(ref reader);
            Assert.True(decoder.ReadTermination(ref reader));
            Assert.Equal(508, decoder.Range);
            H264CabacContext context = new(0, false);
            switch (operation)
            {
                case 0:
                    decoder.ReadBin(ref reader, ref context);
                    break;
                case 1:
                    decoder.ReadBypass(ref reader);
                    break;
                case 2:
                    decoder.ReadBypassBits(ref reader, 0);
                    break;
                case 3:
                    decoder.ReadTermination(ref reader);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        private static void ReadPastEnd()
        {
            H264BitReader reader = new([254, 255]);
            H264CabacDecoder decoder = new(ref reader);
            H264CabacContext context = new(63, false);
            Assert.True(decoder.ReadBin(ref reader, ref context));
            Assert.True(reader.ByteAligned);
            decoder.ReadBin(ref reader, ref context);
        }

        private static void ReadBypassWidth(int count)
        {
            H264BitReader reader = new([0, 0]);
            H264CabacDecoder decoder = new(ref reader);
            decoder.ReadBypassBits(ref reader, count);
        }
    }
}
