// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Adts;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class AdtsFrameHeaderTests
    {
        [Theory]
        [InlineData("FFF15080013FFC", 44100, 2, 9, false, "1210")]
        [InlineData("FFF14C40011FFC", 48000, 1, 8, false, "1188")]
        [InlineData("FFF05080017FFC", 44100, 2, 11, true, "1210")]
        [InlineData("FFF95080013FFC", 44100, 2, 9, false, "1210")]
        public void LiteralHeadersDeclareCoreConfiguration(string hex, int rate, int channels,
            int bytes, bool crc, string configuration)
        {
            byte[] data = Convert.FromHexString(hex);
            AdtsFrameHeader header = AdtsFrameHeader.Read(data);
            Assert.Equal(rate, header.SampleRate);
            Assert.Equal(channels, header.ChannelConfiguration);
            Assert.Equal(bytes, header.FrameBytes);
            Assert.Equal(crc, header.HasCrc);
            Assert.Equal(crc ? 9 : 7, header.HeaderBytes);
            Assert.Equal(1, header.RawDataBlockCount);
            Assert.Equal(2, header.AudioObjectType);
            Assert.Equal(configuration, Convert.ToHexString(header.ToAudioSpecificConfig()));
            Assert.True(new AdtsReaderFactory().CanRead(data));
        }

        [Theory]
        [InlineData("")]
        [InlineData("FFF15080013F")]
        [InlineData("FFFB9064000000")]
        [InlineData("FFF17480013FFC")]
        [InlineData("FFF17880013FFC")]
        [InlineData("FFF17C80013FFC")]
        [InlineData("FFF9D080013FFC")]
        [InlineData("FFF1508000FFFC")]
        [InlineData("FFF05080013FFC")]
        public void TruncatedReservedAndImpossibleHeadersFail(string hex)
        {
            byte[] data = Convert.FromHexString(hex);
            Assert.Throws<InvalidDataException>(() => AdtsFrameHeader.Read(data));
        }

        [Fact]
        public void MultipleBlocksAccountForTheirPositionTableAndCrcWords()
        {
            AdtsFrameHeader header = AdtsFrameHeader.Read(Convert.FromHexString("FFF05080037FFF"));
            Assert.Equal(4, header.RawDataBlockCount);
            Assert.Equal(15, header.HeaderBytes);
            Assert.Equal(27, header.FrameBytes);
        }
    }
}
