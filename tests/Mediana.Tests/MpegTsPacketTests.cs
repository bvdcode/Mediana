// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;

namespace Mediana.Tests
{
    public class MpegTsPacketTests
    {
        [Fact]
        public void ProgramTablesMatchKnownPatAndValidMpegCrc()
        {
            byte[] packet = new byte[188];
            MpegTsTables.Pat(packet, 0);
            Assert.Equal(Convert.FromHexString("474000100000B00D0001C100000001F0002AB104B2"), packet[..21]);
            Assert.Equal(0x0376E6E7u, MpegTsTables.Crc("123456789"u8));
            Assert.Equal(0u, MpegTsTables.Crc(packet.AsSpan(5, 16)));
            Assert.All(packet[21..], value => Assert.Equal(0xFF, value));
            MpegTsTables.Pmt(packet, 7, 0x100, [0x1B, 0x0F]);
            Assert.Equal(Convert.FromHexString("475000170002B0170001C10000E100F0001BE100F0000FE101F000"), packet[..27]);
            Assert.Equal(0u, MpegTsTables.Crc(packet.AsSpan(5, 26)));
            Assert.All(packet[31..], value => Assert.Equal(0xFF, value));
        }

        [Fact]
        public void PesHasExactTimestampPrefixesMarkersAndFiniteAudioLength()
        {
            byte[] header = new byte[19];
            int length = MpegTsPes.Write(header, 0xC0, 100, 90000, 90000, false);
            Assert.Equal(14, length);
            Assert.Equal(Convert.FromHexString("000001C0006C848005210005BF21"), header[..length]);
            length = MpegTsPes.Write(header, 0xE0, 100000, 90000, 45000, true);
            Assert.Equal(19, length);
            Assert.Equal(Convert.FromHexString("000001E0000084C00A310005BF211100035F91"), header);
            byte[] wrapped = new byte[19];
            MpegTsPes.Write(wrapped, 0xE0, 100000, (1L << 33) + 90000, (1L << 33) + 45000, true);
            Assert.Equal(header, wrapped);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(175)]
        [InlineData(176)]
        [InlineData(177)]
        [InlineData(182)]
        [InlineData(183)]
        [InlineData(184)]
        [InlineData(185)]
        [InlineData(368)]
        public void PayloadAndAdaptationStuffingMeetEveryBoundary(int size)
        {
            byte[] input = Enumerable.Range(0, size).Select(index => (byte)index).ToArray();
            byte[] packet = new byte[188];
            foreach (long? pcr in new long?[] { null, 90000 })
            {
                int consumed = MpegTsPacket.Payload(packet, 0x101, 15, true, input, pcr);
                Assert.Equal(Math.Min(size, pcr.HasValue ? 176 : 184), consumed);
                Assert.Equal(0x47, packet[0]);
                Assert.Equal(0x41, packet[1]);
                Assert.Equal(1, packet[2]);
                Assert.Equal(15, packet[3] & 15);
                Assert.Equal(input[..consumed], packet[^consumed..]);
                if (consumed < 184)
                {
                    Assert.Equal(183 - consumed, packet[4]);
                }
                if (pcr.HasValue)
                {
                    Assert.Equal(0x10, packet[5]);
                    Assert.Equal(Convert.FromHexString("0000AFC87E00"), packet[6..12]);
                    Assert.All(packet[12..^consumed], value => Assert.Equal(0xFF, value));
                }
            }
        }

        [Fact]
        public void PcrOnlyPacketCarriesNoPayloadAndPreservesCounter()
        {
            byte[] packet = new byte[188];
            MpegTsPacket.Clock(packet, 0x100, 6, 90000);
            Assert.Equal(Convert.FromHexString("47010026B7100000AFC87E00"), packet[..12]);
            Assert.All(packet[12..], value => Assert.Equal(0xFF, value));
        }
    }
}
