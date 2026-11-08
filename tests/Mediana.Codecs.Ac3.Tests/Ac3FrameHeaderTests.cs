// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3FrameHeaderTests
    {
        [Theory]
        [InlineData("mono.ac3", 48000, 1, 384, 1536, false)]
        [InlineData("stereo-44100.ac3", 44100, 2, 834, 1536, false)]
        [InlineData("surround-32000.ac3", 32000, 6, 1536, 1536, false)]
        [InlineData("stereo.eac3", 48000, 2, 768, 1536, true)]
        [InlineData("enhanced-one.eac3", 48000, 1, 2048, 256, true)]
        [InlineData("enhanced-two.eac3", 48000, 1, 2048, 512, true)]
        [InlineData("enhanced-three.eac3", 48000, 1, 2048, 768, true)]
        public void ReadsCompleteHeaderWithoutPayload(string name, int rate, int channels, int bytes, int samples, bool enhanced)
        {
            byte[] input = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name));
            Ac3FrameHeader header = Ac3FrameHeader.Read(input.AsSpan(0, Ac3FrameHeader.HeaderBytes));
            Assert.Equal(rate, header.SampleRate); Assert.Equal(channels, header.ChannelCount);
            Assert.Equal(bytes, header.FrameBytes); Assert.Equal(samples, header.SamplesPerFrame); Assert.Equal(enhanced, header.IsEnhanced);
            Assert.Equal(channels, header.GetChannelOrder().Length);
            if (channels == 6) { Assert.Equal(["front-left", "front-center", "front-right", "side-left", "side-right", "low-frequency"], header.GetChannelOrder()); }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        public void RejectsTruncatedHeaders(int length) => Assert.Throws<InvalidDataException>(() => Ac3FrameHeader.Read(new byte[length]));

        [Fact]
        public void RejectsReservedSampleRatesAndVersions()
        {
            byte[] header = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "mono.ac3"))[..8];
            header[4] |= 0xc0;
            Assert.Throws<InvalidDataException>(() => Ac3FrameHeader.Read(header));
            header[4] &= 0x3f; header[5] = 9 << 3;
            Assert.Throws<NotSupportedException>(() => Ac3FrameHeader.Read(header));
            header[5] = 16 << 3; header[4] |= 0xc0;
            Assert.Throws<InvalidDataException>(() => Ac3FrameHeader.Read(header));
            header[4] &= 0x3f; header[2] = 0xc0;
            Assert.Throws<InvalidDataException>(() => Ac3FrameHeader.Read(header));
        }
    }
}
