// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.IO;

namespace Mediana.Tests
{
    public class StreamMetadataTests
    {
        [Fact]
        public void AudioMetadataOwnsAndValidatesItsChannelOrder()
        {
            string[] channels = ["front-left", "front-right"];
            AudioStreamMetadata metadata = new(48000, 2, 24, channels);
            channels[0] = "changed";
            Assert.Equal(MediaKind.Audio, metadata.Kind);
            Assert.Equal(48000, metadata.SampleRate);
            Assert.Equal(2, metadata.ChannelCount);
            Assert.Equal(24, metadata.BitsPerSample);
            Assert.Equal(["front-left", "front-right"], metadata.ChannelOrder!);
            IList<string> order = Assert.IsAssignableFrom<IList<string>>(metadata.ChannelOrder);
            Assert.Throws<NotSupportedException>(() => order[0] = "changed");
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioStreamMetadata(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioStreamMetadata(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioStreamMetadata(1, 1, 0));
            Assert.Throws<ArgumentException>(() => new AudioStreamMetadata(1, 2, channelOrder: ["left"]));
            Assert.Throws<ArgumentException>(() => new AudioStreamMetadata(1, 2, channelOrder: ["left", "left"]));
            Assert.Throws<ArgumentException>(() => new AudioStreamMetadata(1, 1, channelOrder: [" "]));
            Assert.Throws<ArgumentException>(() => new AudioStreamMetadata(1, 1, channelOrder: [null!]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixtureStreamMetadata((MediaKind)100));
            AudioStreamMetadata unknown = new(1, 1);
            Assert.Null(unknown.BitsPerSample);
            Assert.Null(unknown.ChannelOrder);
        }

        [Fact]
        public async Task RegisteredMetadataIsIndependentOfDecodersPacketsAndSourceLifetime()
        {
            FixtureContainerFactory factory = new();
            FixtureStreamMetadata expected = new(MediaKind.Video);
            EncodedStreamInfo? inspected = null;
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                .AddStreamMetadataReader(new FixtureStreamMetadataReader("fixture.header", "fixture.rgba", stream =>
                {
                    inspected = stream;
                    return expected;
                })).Build();
            MediaInfo info;
            await using (ShortReadSource source = new([.. "TEST"u8, 11, 22]))
            {
                info = await engine.ProbeAsync(source);
                Assert.False(source.IsDisposed);
            }
            Assert.Empty(engine.VideoDecoderImplementations);
            Assert.Empty(engine.AudioDecoderImplementations);
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.Same(info.Streams[0], inspected);
            Assert.Same(expected, Assert.Single(info.StreamMetadata).Value);
            Assert.Same(expected, info.StreamMetadata[0]);
            IDictionary<int, StreamMetadata> snapshot = Assert.IsAssignableFrom<IDictionary<int, StreamMetadata>>(info.StreamMetadata);
            Assert.Throws<NotSupportedException>(() => snapshot.Clear());
        }

        [Fact]
        public async Task UnknownCodecAndUnsupportedConfigurationRemainAbsent()
        {
            int calls = 0;
            MediaEngineBuilder builder = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory());
            builder.AddStreamMetadataReader(new FixtureStreamMetadataReader("unrelated", "other", _ =>
                throw new InvalidOperationException("An unrelated codec must not be inspected.")));
            MediaEngine before = builder.Build();
            builder.AddStreamMetadataReader(new FixtureStreamMetadataReader("fixture.header", "fixture.rgba", _ =>
            {
                calls++;
                return null;
            }));
            await using ShortReadSource source = new([.. "TEST"u8]);
            Assert.Empty((await before.ProbeAsync(source)).StreamMetadata);
            Assert.Equal(0, calls);
            await using ShortReadSource second = new([.. "TEST"u8]);
            Assert.Empty((await builder.Build().ProbeAsync(second)).StreamMetadata);
            Assert.Equal(1, calls);
            Assert.Equal(["unrelated"], before.StreamMetadataReaderImplementations);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task InvalidReaderResultsAndMalformedSyntaxReleaseTheContainer(bool wrongKind)
        {
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                .AddStreamMetadataReader(new FixtureStreamMetadataReader("fixture.header", "fixture.rgba", _ =>
                {
                    if (wrongKind)
                    {
                        return new AudioStreamMetadata(48000, 1);
                    }
                    throw new InvalidDataException("Malformed header.");
                })).Build();
            await using ShortReadSource source = new([.. "TEST"u8]);
            if (wrongKind)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.ProbeAsync(source));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await engine.ProbeAsync(source));
            }
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task CancellationDuringInspectionDisposesTheReaderAndDoesNotReturnPartialMetadata()
        {
            using CancellationTokenSource cancellation = new();
            FixtureContainerFactory factory = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                .AddStreamMetadataReader(new FixtureStreamMetadataReader("fixture.header", "fixture.rgba", _ =>
                {
                    cancellation.Cancel();
                    return new FixtureStreamMetadata(MediaKind.Video);
                })).Build();
            await using ShortReadSource source = new([.. "TEST"u8]);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.ProbeAsync(source,
                cancellationToken: cancellation.Token));
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public void RegistryRejectsAmbiguityAndInvalidIdentities()
        {
            FixtureStreamMetadataReader reader = new("reader", "codec", _ => null);
            MediaEngineBuilder builder = new();
            Assert.Empty(builder.Build().StreamMetadataReaderImplementations);
            builder.AddStreamMetadataReader(reader);
            Assert.Throws<ArgumentException>(() => builder.AddStreamMetadataReader(reader));
            Assert.Throws<ArgumentException>(() => builder.AddStreamMetadataReader(new FixtureStreamMetadataReader("other", "codec", _ => null)));
            Assert.Throws<ArgumentException>(() => builder.AddStreamMetadataReader(new FixtureStreamMetadataReader("reader", "other", _ => null)));
            Assert.Throws<ArgumentNullException>(() => builder.AddStreamMetadataReader(null!));
            Assert.Throws<ArgumentException>(() => builder.AddStreamMetadataReader(new FixtureStreamMetadataReader(" ", "codec", _ => null)));
            Assert.Throws<ArgumentException>(() => builder.AddStreamMetadataReader(new FixtureStreamMetadataReader("reader", " ", _ => null)));
        }
    }
}
