// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;
using Mediana.Tests;

namespace Mediana.Codecs.Opus.Tests
{
    public class OpusPacketConfigurationTests
    {
        [Fact]
        public void EveryTocByteMatchesIndependentChannelDurationAndBandwidthDeclarations() =>
            Assert.Equal(256, OpusConfigurationTestInput.Verify(Path.Combine(AppContext.BaseDirectory, "Data")));

        [Theory]
        [InlineData(0, OpusCodingMode.Silk, 480)]
        [InlineData(3, OpusCodingMode.Silk, 2880)]
        [InlineData(8, OpusCodingMode.Silk, 480)]
        [InlineData(11, OpusCodingMode.Silk, 2880)]
        [InlineData(12, OpusCodingMode.Hybrid, 480)]
        [InlineData(15, OpusCodingMode.Hybrid, 960)]
        [InlineData(16, OpusCodingMode.Celt, 120)]
        [InlineData(31, OpusCodingMode.Celt, 960)]
        public void CodingModesUseTheirDeclaredFrameClock(int configuration, OpusCodingMode mode, int samples)
        {
            OpusPacketConfiguration info = OpusPacketConfiguration.FromToc((byte)(configuration << 3));
            Assert.Equal(mode, info.CodingMode);
            Assert.Equal(samples, info.SamplesPerFrame);
        }

        [Fact]
        public void FramePackingDoesNotChangeCodingConfiguration()
        {
            OpusPacketConfiguration first = OpusPacketConfiguration.FromToc(128);
            Assert.Equal(first, OpusPacketConfiguration.FromToc(131));
            Assert.NotEqual(first, OpusPacketConfiguration.FromToc(132));
        }

        [Fact]
        public void StereoOutputCanCarryAMonoCodedPacket()
        {
            byte[] header = new byte[19];
            "OpusHead"u8.CopyTo(header);
            header[8] = 1; header[9] = 2;
            OpusConfiguration output = OpusConfiguration.Read(header);
            OpusPacketConfiguration packet = OpusPacketConfiguration.FromToc(128);
            Assert.Equal(2, output.ChannelCount);
            Assert.Equal(1, packet.CodedChannelCount);
        }
    }
}
