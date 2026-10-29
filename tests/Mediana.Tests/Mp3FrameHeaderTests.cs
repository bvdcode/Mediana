using System.Buffers.Binary;
using Mediana.Core.Audio;

namespace Mediana.Tests
{
    public class Mp3FrameHeaderTests
    {
        [Theory]
        [InlineData(0xfffb9064U, MpegAudioVersion.Mpeg1, 44100, 128000, 417, 1152, 2)]
        [InlineData(0xfffb9264U, MpegAudioVersion.Mpeg1, 44100, 128000, 418, 1152, 2)]
        [InlineData(0xfff394c0U, MpegAudioVersion.Mpeg2, 24000, 80000, 240, 576, 1)]
        [InlineData(0xffe318c0U, MpegAudioVersion.Mpeg25, 8000, 8000, 72, 576, 1)]
        public void HeadersDefineExactFrameSizeSampleCountAndChannelConfiguration(uint word, MpegAudioVersion version,
            int rate, int bitRate, int bytes, int samples, int channels)
        {
            byte[] data = Bytes(word);
            Assert.True(Mp3FrameHeader.HasLayer3Sync(data));
            Mp3FrameHeader header = Mp3FrameHeader.Read(data);
            Assert.Equal(version, header.Version);
            Assert.Equal(rate, header.SampleRate);
            Assert.Equal(bitRate, header.BitRate);
            Assert.Equal(bytes, header.FrameBytes);
            Assert.Equal(samples, header.SamplesPerFrame);
            Assert.Equal(channels, header.ChannelCount);
        }

        [Theory]
        [InlineData(0xffeb9064U)]
        [InlineData(0xfffbfc64U)]
        [InlineData(0xfffb9c64U)]
        [InlineData(0xfffb9066U)]
        [InlineData(0x7ffb9064U)]
        [InlineData(0xfff99064U)]
        public void ReservedAndInvalidFieldsAreRejected(uint word)
        {
            Assert.Throws<InvalidDataException>(() => Mp3FrameHeader.Read(Bytes(word)));
        }

        [Fact]
        public void UnsupportedLayersAndFreeFormatAreExplicit()
        {
            Assert.Throws<NotSupportedException>(() => Mp3FrameHeader.Read(Bytes(0xfffd9064)));
            Assert.Throws<NotSupportedException>(() => Mp3FrameHeader.Read(Bytes(0xfffb0064)));
        }

        private static byte[] Bytes(uint word)
        {
            byte[] data = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(data, word);
            return data;
        }
    }
}
