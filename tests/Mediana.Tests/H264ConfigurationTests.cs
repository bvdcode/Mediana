using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264ConfigurationTests
    {
        [Theory]
        [InlineData("avc-tail.mov", 66, 80, 48, false)]
        [InlineData("avc-aac.mp4", 100, 64, 48, true)]
        public async Task ParsesRealAvcConfiguration(string file, int profile, int width, int height, bool cabac)
        {
            byte[] configuration = await ReadConfigurationAsync(file);
            H264Configuration parsed = H264Configuration.Parse(configuration, new H264DecoderOptions());
            H264SequenceParameterSet sequence = Assert.Single(parsed.Sequences.Values);
            H264PictureParameterSet picture = Assert.Single(parsed.Pictures.Values);
            Assert.Equal(4, parsed.LengthSize);
            Assert.Equal(profile, sequence.Profile);
            Assert.Equal(width, sequence.Width);
            Assert.Equal(height, sequence.Height);
            Assert.Equal(8, sequence.LumaBitDepth);
            Assert.Equal(8, sequence.ChromaBitDepth);
            Assert.Equal(1, sequence.ChromaFormat);
            Assert.True(sequence.FrameMbsOnly);
            Assert.Equal(sequence.Id, picture.SequenceId);
            Assert.Equal(cabac, picture.Cabac);
        }

        [Fact]
        public async Task RejectsTruncatedConfigurationsAndReservedLengthSize()
        {
            byte[] configuration = await ReadConfigurationAsync("avc-tail.mov");
            for (int length = 0; length < configuration.Length; length++)
            {
                byte[] truncated = configuration[..length];
                Assert.Throws<InvalidDataException>(() => H264Configuration.Parse(truncated, new H264DecoderOptions()));
            }
            configuration[4] = 0xFE;
            Assert.Throws<InvalidDataException>(() => H264Configuration.Parse(configuration, new H264DecoderOptions()));
        }

        [Fact]
        public async Task LimitsConfigurationParameterSetAndPictureSizes()
        {
            byte[] configuration = await ReadConfigurationAsync("avc-tail.mov");
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(configuration,
                new H264DecoderOptions { MaxConfigurationBytes = 8 }));
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(configuration,
                new H264DecoderOptions { MaxParameterSetBytes = 2 }));
            Assert.Throws<NotSupportedException>(() => H264Configuration.Parse(configuration,
                new H264DecoderOptions { MaxPicturePixels = 16 }));
        }

        private static async Task<byte[]> ReadConfigurationAsync(string name)
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(name));
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            return reader.Streams[0].Configuration.ToArray();
        }
    }
}
