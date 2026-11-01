// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264ColorDescriptionWriterTests
    {
        private const string Baseline = "01000010 00000000 00011110 1 1 1 1 010 0 00111 00101 1 1 1 1 1 010 010";
        private static readonly string High = "11110100 00000000 00101000 1 00100 1 00111 00111 1 1 1 " +
            H264TestBits.Ue(16) + " 00000000000 1 010 0 " + H264TestBits.Ue(3) + H264TestBits.Ue(2) +
            H264TestBits.Ue(2) + H264TestBits.Ue(5) + H264TestBits.Ue(4) + " 011 1 00111 011 0 1 1 1 1 010 1 010";
        private const string DetailedSuffix = "1 011 00100 1 00000000000000000000000000000001 00000000000000000000000000110010 1 " +
            "1 1 00000000 111 00000000000000000000 1 1 00000000 111 00000000000000000000 1 1 1 1 1 1 00101 00110 010 011";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AddsVuiAndWritesExactCodesWithoutChangingPictureSyntax(bool fullRange)
        {
            MediaBufferAllocator allocator = new(4096);
            byte[] nal = Nal(Baseline + " 0");
            using (MediaBuffer rewritten = H264ColorDescriptionWriter.Rewrite(nal, new(6, 6, 6, fullRange), allocator, 512))
            {
                string signal = fullRange ? "1 101 1 1" : "1 101 0 1";
                Assert.Equal(Nal(Baseline + " 1 00 " + signal + " 00000110 00000110 00000110 000000"), rewritten.Memory.ToArray());
                H264SequenceParameterSet original = H264SequenceParser.Parse(nal);
                H264SequenceParameterSet result = H264SequenceParser.Parse(rewritten.Memory.Span);
                Assert.Equal(original, result with { Vui = null });
                Assert.Equal(fullRange, result.Vui!.FullRange);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PreservesAspectOverscanHrdTimingRestrictionsAndExtendedPictureSyntax(bool extended)
        {
            string prefix = extended ? High : Baseline;
            const string beforeSignal = "1 11111111 0000000000000100 0000000000000011 1 0";
            byte[] nal = Nal(prefix + " 1 " + beforeSignal + " 1 010 1 0 " + DetailedSuffix);
            byte[] expected = Nal(prefix + " 1 " + beforeSignal + " 1 010 1 1 00001001 00010000 00001001 " + DetailedSuffix);
            MediaBufferAllocator allocator = new(4096);
            using (MediaBuffer rewritten = H264ColorDescriptionWriter.Rewrite(nal, new(9, 16, 9), allocator, 512))
            {
                Assert.Equal(expected, rewritten.Memory.ToArray());
                H264SequenceParameterSet original = H264SequenceParser.Parse(nal);
                H264SequenceParameterSet result = H264SequenceParser.Parse(rewritten.Memory.Span);
                Assert.Equal(original with { Vui = original.Vui! with { ColorPrimaries = 9, TransferCharacteristics = 16, MatrixCoefficients = 9 } }, result);
                Assert.Contains((byte)3, rewritten.Memory.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("000000000", null, false)]
        [InlineData("00110110000000", null, true)]
        [InlineData("00110110000000", false, false)]
        [InlineData("000000000", true, true)]
        public void PreservesOrOverridesRangeAndExplicitUnspecifiedCodes(string vui, bool? sourceRange, bool expectedRange)
        {
            MediaBufferAllocator allocator = new(4096);
            using (MediaBuffer rewritten = H264ColorDescriptionWriter.Rewrite(Nal(Baseline + " 1 " + vui), new(2, 2, 2, sourceRange), allocator, 512))
            {
                H264VuiParameters result = H264SequenceParser.Parse(rewritten.Memory.Span).Vui!;
                Assert.Equal(expectedRange, result.FullRange);
                Assert.Equal(2, result.ColorPrimaries);
                Assert.Equal(2, result.TransferCharacteristics);
                Assert.Equal(2, result.MatrixCoefficients);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0, 1, 1)]
        [InlineData(65535, 1, 1)]
        [InlineData(1, 255, 1)]
        [InlineData(1, 1, 255)]
        public void RejectsUnrepresentableColorsWithoutAllocating(int primaries, int transfer, int matrix)
        {
            MediaBufferAllocator allocator = new(4096);
            Assert.Throws<NotSupportedException>(() => H264ColorDescriptionWriter.Rewrite(Nal(Baseline + " 0"), new(primaries, transfer, matrix), allocator, 512));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ValidatesSyntaxAndReleasesStorageOnLimits()
        {
            MediaBufferAllocator allocator = new(4096);
            CicpColorDescription color = new(1, 1, 1);
            byte[] nal = Nal(Baseline + " 0");
            Assert.Throws<NotSupportedException>(() => H264ColorDescriptionWriter.Rewrite(nal, color, allocator, 8));
            Assert.Throws<MediaMemoryLimitException>(() => H264ColorDescriptionWriter.Rewrite(nal, color, new(7), 512));
            Assert.Throws<InvalidDataException>(() => H264ColorDescriptionWriter.Rewrite(nal.AsSpan(0, nal.Length - 1), color, allocator, 512));
            nal[1] = 255;
            Assert.Throws<NotSupportedException>(() => H264ColorDescriptionWriter.Rewrite(nal, color, allocator, 512));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BitPositionCountsRbspBitsAcrossEmulationPreventionAndPartialReads()
        {
            H264BitReader input = new([0, 0, 3, 1, 0, 0, 3, 2]);
            Assert.Equal(0, input.BitPosition);
            input.ReadBits(19);
            Assert.Equal(19, input.BitPosition);
            input.ReadBits(21);
            Assert.Equal(40, input.BitPosition);
            input.ReadBits(8);
            Assert.Equal(48, input.BitPosition);
        }

        private static byte[] Nal(string bits) => [0x67, .. H264TestBits.Rbsp(bits)];
    }
}
