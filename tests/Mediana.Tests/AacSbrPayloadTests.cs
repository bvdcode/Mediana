// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacSbrPayloadTests
    {
        [Theory]
        [InlineData("964002001400000000C0", false)]
        [InlineData("EDE590008005000000003000", true)]
        public void ProtectedAndUnprotectedPayloadsReconstructTheSameEnvelope(string hex, bool crc)
        {
            AacSbrElement element = new(1, 48000);
            Read(element, Convert.FromHexString(hex), crc);
            Assert.Equal(new AacSbrHeader(false, 5, 9, 0, 2, true, 2, 2, 2, true, true), element.Header);
            Assert.True(element.FrequencyReset);
            Assert.Equal(16, element.Tables.HighCount);
            Assert.Equal(4, element.Tables.NoiseCount);
            Assert.All(element.Channels[0].Energy[..16], energy => Assert.Equal(16384, energy));
            Assert.All(element.Channels[0].NoiseRatio[..4], ratio => Assert.Equal(1, ratio));
        }

        [Fact]
        public void CrcProtectsEveryPayloadBitIncludingAlignmentPadding()
        {
            byte[] valid = Convert.FromHexString("EDE590008005000000003000");
            for (int bit = 0; bit < valid.Length * 8; bit++)
            {
                byte[] corrupted = (byte[])valid.Clone();
                corrupted[bit / 8] ^= (byte)(1 << (7 - bit % 8));
                AacSbrElement element = new(1, 48000);
                InvalidDataException error = Assert.Throws<InvalidDataException>(() => Read(element, corrupted, true));
                Assert.Contains("checksum", error.Message, StringComparison.Ordinal);
                Assert.Null(element.Header);
                Assert.False(element.Channels[0].HasPreviousEnvelope);
            }
        }

        [Theory]
        [InlineData("964002001400000000C0", false)]
        [InlineData("EDE590008005000000003000", true)]
        public void EveryTruncatedPrefixIsRejected(string hex, bool crc)
        {
            byte[] valid = Convert.FromHexString(hex);
            for (int length = 0; length < valid.Length; length++)
            {
                AacSbrElement element = new(1, 48000);
                byte[] truncated = valid[..length];
                Assert.Throws<InvalidDataException>(() => Read(element, truncated, crc));
            }
        }

        [Fact]
        public void TimeDifferentialEnvelopeAndNoiseUsePreviousFrameState()
        {
            AacTestBitWriter writer = new();
            writer.Write(0, 1).Write(0, 1).Write(0, 2).Write(0, 2).Write(1, 1)
                .Write(1, 1).Write(1, 1).Write(0, 8);
            for (int band = 0; band < 16; band++)
            {
                writer.Write(0, 2);
            }
            writer.Write(0, 4).Write(0, 1).Write(0, 1);
            byte[] differential = writer.ToArray();
            AacSbrElement missingHeader = new(1, 48000);
            Assert.Throws<InvalidDataException>(() => Read(missingHeader, differential, false));
            AacSbrElement element = new(1, 48000);
            Read(element, Convert.FromHexString("964002001400000000C0"), false);
            double[] energy = element.Channels[0].Energy[..16];
            double[] noise = element.Channels[0].NoiseRatio[..4];
            element.Present = false;
            Read(element, differential, false);
            Assert.False(element.FrequencyReset);
            Assert.Equal(energy, element.Channels[0].Energy[..16]);
            Assert.Equal(noise, element.Channels[0].NoiseRatio[..4]);
        }

        private static void Read(AacSbrElement element, byte[] data, bool crc)
        {
            AacBitReader bits = new(data);
            element.Read(ref bits, crc);
            Assert.Equal(0, bits.Remaining);
        }
    }
}
