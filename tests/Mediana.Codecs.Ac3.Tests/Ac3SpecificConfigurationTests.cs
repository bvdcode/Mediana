// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Formats;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Ac3.Tests
{
    public class Ac3SpecificConfigurationTests
    {
        [Fact]
        public void Ac3SpecificBoxDescribesTheActualCodedLayout()
        {
            byte[] bytes = Dac3(48000, 7, true);
            Ac3SpecificConfiguration configuration = Ac3SpecificConfiguration.Read(bytes);
            Assert.Equal(48000, configuration.SampleRate); Assert.Equal(8, configuration.BitstreamId);
            Assert.Equal(6, configuration.ChannelCount);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "ac3", StreamFormats.Ac3Mp4, new(1, 48000), bytes);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(new Ac3StreamMetadataReader().Read(stream));
            Assert.Equal(configuration.GetChannelOrder(), metadata.ChannelOrder);
            Assert.Null(new Ac3DecoderFactory().GetUnsupportedReason(stream));
        }

        [Fact]
        public void Eac3SpecificBoxRetainsEverySubstreamAndDescribesDependentChannelsWithoutClaimingDecodeSupport()
        {
            byte[] data = [0x0c, 1, 0x20, 15, 2, 2, 0x20, 4, 0, 0xaa, 0x55];
            Eac3SpecificConfiguration configuration = Eac3SpecificConfiguration.Read(data);
            Assert.Equal(384, configuration.DataRateKilobitsPerSecond); Assert.Equal(2, configuration.Substreams.Count);
            Assert.Equal(8, configuration.Substreams[0].ChannelCount);
            Assert.Equal(2, configuration.Substreams[0].DependentChannelLocations);
            Assert.Equal(2, configuration.Substreams[1].ChannelCount);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "eac3", StreamFormats.Eac3Mp4, new(1, 48000), data);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(new Eac3StreamMetadataReader().Read(stream));
            Assert.Equal(8, metadata.ChannelCount); Assert.Null(metadata.ChannelOrder);
            Assert.Contains("independent", new Eac3DecoderFactory().GetUnsupportedReason(stream));
            data[1] = 0;
            stream = new(0, MediaKind.Audio, "eac3", StreamFormats.Eac3Mp4, new(1, 48000), data);
            Assert.Null(new Eac3DecoderFactory().GetUnsupportedReason(stream));
        }

        [Fact]
        public void FactoriesValidateMp4FrameDeclarationsAndTheRequiredSixBlockAccessUnit()
        {
            byte[] bytes = Ac3SyntheticFrame.Create(true, false);
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "eac3", StreamFormats.Eac3Mp4, new(1, 48000), [0, 0, 0x20, 2, 0]);
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new Eac3DecoderFactory(false).Create(stream, allocator))
            {
                using MediaPacket packet = Ac3DecoderTests.Packet(bytes, allocator, count: 1000);
                decoder.TrySend(packet); decoder.CompleteInput(); using AudioFrame pcm = decoder.Receive().Value;
                Assert.Equal(1000, pcm.SamplesPerChannel);
                decoder.Reset();
                byte[] shortFrame = Ac3SyntheticFrame.Create(true, false, 1);
                using MediaPacket incomplete = Ac3DecoderTests.Packet(shortFrame, allocator);
                Assert.Throws<InvalidDataException>(() => decoder.TrySend(incomplete));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void Mp4ConfigurationMismatchFailsBeforePcmIsProduced()
        {
            byte[] bytes = Ac3DecoderTests.ReadFirstFrame("mono.ac3");
            MediaBufferAllocator allocator = new(1024 * 1024);
            EncodedStreamInfo stream = new(0, MediaKind.Audio, "ac3", StreamFormats.Ac3Mp4, new(1, 48000), Dac3(48000, 2, false));
            using (IMediaCodec<MediaPacket, AudioFrame> decoder = new Ac3DecoderFactory().Create(stream, allocator))
            using (MediaPacket packet = Ac3DecoderTests.Packet(bytes, allocator))
            { Assert.Throws<InvalidDataException>(() => decoder.TrySend(packet)); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void SelfDescribingRepresentationsNeverPretendToCarryExternalMetadata()
        {
            EncodedStreamInfo raw = new(0, MediaKind.Audio, "ac3", StreamFormats.Ac3, new(1, 48000));
            Assert.Null(new Ac3StreamMetadataReader().Read(raw));
            EncodedStreamInfo invalid = new(0, MediaKind.Audio, "ac3", StreamFormats.Ac3, new(1, 48000), [1]);
            Assert.Throws<InvalidDataException>(() => new Ac3StreamMetadataReader().Read(invalid));
        }

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0, 0 })]
        [InlineData(new byte[] { 0xc0, 0, 0 })]
        [InlineData(new byte[] { 0x10, 0, 1 })]
        public void RejectsInvalidDac3(byte[] bytes) => Assert.Throws<InvalidDataException>(() => Ac3SpecificConfiguration.Read(bytes));

        [Theory]
        [InlineData(new byte[] { })]
        [InlineData(new byte[] { 0, 0, 0x20, 2 })]
        [InlineData(new byte[] { 0, 0, 0x21, 2, 0 })]
        [InlineData(new byte[] { 0, 0, 0x20, 2, 1 })]
        [InlineData(new byte[] { 0, 0, 0x20, 2, 2 })]
        public void RejectsInvalidDec3(byte[] bytes) => Assert.Throws<InvalidDataException>(() => Eac3SpecificConfiguration.Read(bytes));

        private static byte[] Dac3(int rate, int mode, bool lfe)
        {
            int value = ((rate switch { 48000 => 0, 44100 => 1, 32000 => 2, _ => throw new ArgumentOutOfRangeException(nameof(rate)) }) << 22)
                | 8 << 17 | mode << 11 | (lfe ? 1 << 10 : 0) | 6 << 5;
            return [(byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }
    }
}
