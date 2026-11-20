// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264QuantizerTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(14)]
        public void EveryDeltaMatchesTheNormativeSignedQuantizerEquation(int bitDepth)
        {
            H264Quantizer quantizer = new(bitDepth);
            int offset = 6 * (bitDepth - 8);
            for (int syntax = -offset; syntax <= 51; syntax++)
            {
                int prime = quantizer.FromSyntax(syntax);
                Assert.Equal(syntax + offset, prime);
                Assert.Equal(Math.Clamp(syntax, 0, 51), quantizer.ContextValue(prime));
                for (int delta = -(26 + offset / 2); delta <= 25 + offset / 2; delta++)
                {
                    int expected = ((syntax + delta + 52 + 2 * offset) % (52 + offset)) - offset;
                    Assert.Equal(expected, quantizer.ApplyDelta(prime, delta) - offset);
                }
            }
            Assert.Throws<InvalidDataException>(() => quantizer.FromSyntax(-offset - 1));
            Assert.Throws<InvalidDataException>(() => quantizer.FromSyntax(52));
            Assert.Throws<InvalidDataException>(() => quantizer.ApplyDelta(0, quantizer.MinimumDelta - 1));
            Assert.Throws<InvalidDataException>(() => quantizer.ApplyDelta(0, quantizer.MaximumDelta + 1));
        }

        [Theory]
        [InlineData(8, 0)]
        [InlineData(10, -12)]
        [InlineData(12, -24)]
        [InlineData(14, -36)]
        public void PictureParameterSetAcceptsItsFullInitialQuantizerRange(int bitDepth, int minimum)
        {
            H264SequenceParameterSet sequence = new() { LumaBitDepth = bitDepth };
            Dictionary<int, H264SequenceParameterSet> sequences = new() { [0] = sequence };
            Assert.Equal(minimum, H264PictureParser.Parse(Picture(minimum), sequences).InitialQp);
            Assert.Equal(51, H264PictureParser.Parse(Picture(51), sequences).InitialQp);
            Assert.Throws<InvalidDataException>(() => H264PictureParser.Parse(Picture(minimum - 1), sequences));
            Assert.Throws<InvalidDataException>(() => H264PictureParser.Parse(Picture(52), sequences));
        }

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(14)]
        public void ChromaMappingPreservesNegativeSyntaxAndNonlinearHighValues(int bitDepth)
        {
            H264Quantizer quantizer = new(bitDepth);
            int offset = 6 * (bitDepth - 8);
            Assert.Equal(0, quantizer.ChromaValue(-offset, -12));
            Assert.Equal(offset + 29, quantizer.ChromaValue(30, 0));
            Assert.Equal(offset + 32, quantizer.ChromaValue(34, 0));
            Assert.Equal(offset + 39, quantizer.ChromaValue(51, 12));
            for (int value = -offset; value < 30; value++)
            {
                Assert.Equal(value + offset, quantizer.ChromaValue(value, 0));
            }
        }

        [Theory]
        [InlineData(7)]
        [InlineData(15)]
        public void InvalidSamplePrecisionIsRejected(int bitDepth) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new H264Quantizer(bitDepth));

        private static byte[] Picture(int qp)
        {
            int delta = qp - 26;
            int mapped = delta <= 0 ? -2 * delta : 2 * delta - 1;
            string syntax = "1100111000" + H264TestBits.Ue(mapped) + "11100";
            return [0x68, .. H264TestBits.Rbsp(syntax)];
        }
    }
}
