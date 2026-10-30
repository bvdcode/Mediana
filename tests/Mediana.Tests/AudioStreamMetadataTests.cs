// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Codecs.Flac;
using Mediana.Codecs.Mp3;
using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class AudioStreamMetadataTests
    {
        [Fact]
        public async Task RealAudioHeadersMatchIndependentReferencesWithoutDecoders() =>
            await AudioStreamMetadataTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ReadersRejectWrongStreamsAndMalformedConfiguration(int index)
        {
            IStreamMetadataReader reader = CreateReader(index);
            Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
            Assert.Throws<ArgumentException>(() => reader.Read(new EncodedStreamInfo(0, MediaKind.Video, reader.CodecId, new TimeBase(1, 1))));
            Assert.Throws<ArgumentException>(() => reader.Read(new EncodedStreamInfo(0, MediaKind.Audio, "other", new TimeBase(1, 1))));
            Assert.Throws<InvalidDataException>(() => reader.Read(new EncodedStreamInfo(0, MediaKind.Audio, reader.CodecId, new TimeBase(1, 1))));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        [InlineData(5, 5)]
        [InlineData(6, 6)]
        [InlineData(7, 8)]
        public void AacHeadersDoNotInheritDecoderChannelAndBandTableLimits(int configuration, int count)
        {
            EncodedStreamInfo stream = new(19, MediaKind.Audio, "aac", new TimeBase(1, 17),
                AacConfiguration.Create(7350, configuration).ToAudioSpecificConfig());
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(new AacStreamMetadataReader().Read(stream));
            Assert.Equal(7350, metadata.SampleRate);
            Assert.Equal(count, metadata.ChannelCount);
            Assert.Null(metadata.BitsPerSample);
            Assert.Null(metadata.ChannelOrder);
            Assert.NotNull(new AacDecoderFactory().GetUnsupportedReason(stream));
        }

        [Fact]
        public void UnsupportedConfigurationIsNotPresentedAsKnownAudio()
        {
            Assert.Null(new AacStreamMetadataReader().Read(new EncodedStreamInfo(0, MediaKind.Audio, "aac", new TimeBase(1, 1), [0x12, 0x14])));
            Assert.Null(new PcmStreamMetadataReader().Read(new EncodedStreamInfo(0, MediaKind.Audio, "pcm", new TimeBase(1, 1),
                WaveTestInput.Format(tag: 6))));
            Assert.Null(new AiffPcmStreamMetadataReader().Read(new EncodedStreamInfo(0, MediaKind.Audio, "pcm.aiff", new TimeBase(1, 1),
                AiffTestInput.Configuration(compression: "ulaw"))));
        }

        [Fact]
        public async Task ProbeUsesValidPrecisionAndExplicitChannelOrderInsteadOfStorageSize()
        {
            byte[] format = WaveTestInput.Format(32, 6, tag: 0xfffe, validBits: 20, mask: 0x3f);
            await using TrackingRandomAccessSource source = new(WaveTestInput.File(("fmt ", format), ("data", new byte[24])));
            MediaInfo info = await AudioStreamMetadataTestInput.CreateEngine().ProbeAsync(source);
            AudioStreamMetadata audio = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(20, audio.BitsPerSample);
            Assert.Equal(["front-left", "front-right", "front-center", "low-frequency", "back-left", "back-right"], audio.ChannelOrder!);
        }

        [Fact]
        public async Task MetadataReadersDoNotReadPayloadOrGuessVbrBitRateFromOpeningFrame()
        {
            byte[] frame = Mp3TestInput.Frame();
            byte[] bytes = [.. frame, .. new byte[1024 * 1024]];
            await using TrackingRandomAccessSource before = new(bytes, 7);
            await using TrackingRandomAccessSource after = new(bytes, 7);
            MediaInfo basic = await MediaProbeTestInput.CreateEngine().ProbeAsync(before);
            MediaInfo rich = await AudioStreamMetadataTestInput.CreateEngine().ProbeAsync(after);
            Assert.Equal(before.Reads, after.Reads);
            Assert.Equal(basic.Streams[0].Configuration.ToArray(), rich.Streams[0].Configuration.ToArray());
            Assert.Null(rich.Container.DeclaredDuration);
            Assert.Null(rich.Streams[0].CodedDuration);
            Assert.IsType<AudioStreamMetadata>(rich.StreamMetadata[0]);
        }

        [Fact]
        public async Task SharedMetadataReadersSupportConcurrentIndependentProbes()
        {
            MediaEngine engine = AudioStreamMetadataTestInput.CreateEngine();
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Metadata", "lc-surround-48.m4a");
            await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
            {
                await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
                MediaInfo info = await engine.ProbeAsync(source);
                AudioStreamMetadata audio = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
                Assert.Equal(48000, audio.SampleRate);
                Assert.Equal(6, audio.ChannelCount);
            }));
        }

        private static IStreamMetadataReader CreateReader(int index) => index switch
        {
            0 => new PcmStreamMetadataReader(),
            1 => new AiffPcmStreamMetadataReader(),
            2 => new FlacStreamMetadataReader(),
            3 => new Mp3StreamMetadataReader(),
            4 => new AacStreamMetadataReader(),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }
}
