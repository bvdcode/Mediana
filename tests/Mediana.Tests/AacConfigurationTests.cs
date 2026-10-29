// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Tests
{
    public class AacConfigurationTests
    {
        [Theory]
        [InlineData(0, 96000)]
        [InlineData(1, 88200)]
        [InlineData(2, 64000)]
        [InlineData(3, 48000)]
        [InlineData(4, 44100)]
        [InlineData(5, 32000)]
        [InlineData(6, 24000)]
        [InlineData(7, 22050)]
        [InlineData(8, 16000)]
        [InlineData(9, 12000)]
        [InlineData(10, 11025)]
        [InlineData(11, 8000)]
        [InlineData(12, 7350)]
        public void IndexedRatesAndChannelConfigurationsAreExplicit(int index, int rate)
        {
            for (int channels = 1; channels <= 7; channels++)
            {
                byte[] bytes = [(byte)(0x10 | index >> 1), (byte)(index << 7 | channels << 3)];
                AacConfiguration value = AacConfiguration.ReadAudioSpecificConfig(bytes, out string? reason)!;
                Assert.Null(reason);
                Assert.Equal(index, value.FrequencyIndex);
                Assert.Equal(rate, value.SampleRate);
                Assert.Equal(channels, value.ChannelConfiguration);
                Assert.Equal(channels == 7 ? 8 : channels, value.ChannelCount);
                Assert.Equal(1024, value.SamplesPerFrame);
            }
        }

        [Fact]
        public void AbsentSbrExtensionMatchesTheBaseConfiguration()
        {
            AacConfiguration value = AacConfiguration.ReadAudioSpecificConfig([0x11, 0x88, 0x56, 0xe5, 0], out string? reason)!;
            Assert.Null(reason);
            Assert.Equal(48000, value.SampleRate);
            Assert.Equal(1, value.ChannelCount);
        }

        [Theory]
        [InlineData(new byte[] { 0x29, 0x88 })]
        [InlineData(new byte[] { 0x11, 0x80 })]
        [InlineData(new byte[] { 0x11, 0x8c })]
        [InlineData(new byte[] { 0x11, 0x88, 0x56, 0xe5, 0x80 })]
        [InlineData(new byte[] { 0x11, 0x88, 0, 0 })]
        public void UnsupportedToolsDoNotBecomePlainLc(byte[] bytes)
        {
            Assert.Null(AacConfiguration.ReadAudioSpecificConfig(bytes, out string? reason));
            Assert.NotNull(reason);
        }

        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 0x11 })]
        [InlineData(new byte[] { 0x16, 0x88 })]
        [InlineData(new byte[] { 0x17, 0x08 })]
        [InlineData(new byte[] { 0x11, 0x88, 0x56, 0xe5 })]
        [InlineData(new byte[] { 0x11, 0x88, 0x56, 0xe5, 1 })]
        public void TruncatedReservedAndNonzeroPaddingAreRejected(byte[] bytes) =>
            Assert.Throws<InvalidDataException>(() => AacConfiguration.ReadAudioSpecificConfig(bytes, out _));
    }
}
