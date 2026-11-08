// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Tests
{
    public class ContainerStreamMetadataTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ContainerFactsDoNotRequireCodecOrDecoderRegistration(bool registerReader)
        {
            FixtureStreamMetadata expected = new(MediaKind.Video);
            FixtureContainerFactory factory = new(metadata: index => index == 0 ? expected : throw new InvalidOperationException());
            MediaEngineBuilder builder = new MediaEngineBuilder().AddContainer(factory);
            if (registerReader)
            {
                builder.AddStreamMetadataReader(new FixtureStreamMetadataReader("fixture.header", "fixture.rgba", _ => null));
            }
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22]);
            MediaInfo info = await builder.Build().ProbeAsync(source);
            Assert.Same(expected, info.StreamMetadata[0]);
            Assert.Equal(1, factory.LastReader!.MetadataReads);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.True(factory.LastReader.IsDisposed);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task ConfigurationFactsHavePriorityAndAvoidContainerQueries()
        {
            FixtureStreamMetadata expected = new(MediaKind.Video);
            FixtureContainerFactory factory = new(metadata: _ => throw new InvalidOperationException("Unexpected container metadata query."));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory)
                .AddStreamMetadataReader(new FixtureStreamMetadataReader("fixture.header", "fixture.rgba", _ => expected)).Build();
            await using ShortReadSource source = new([.. "TEST"u8]);
            Assert.Same(expected, (await engine.ProbeAsync(source)).StreamMetadata[0]);
            Assert.Equal(0, factory.LastReader!.MetadataReads);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InvalidFactsAndCancellationDisposeTheReader(bool cancel)
        {
            using CancellationTokenSource cancellation = new();
            FixtureContainerFactory factory = new(metadata: _ =>
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    return new FixtureStreamMetadata(MediaKind.Video);
                }
                return new AudioStreamMetadata(48000, 1);
            });
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8]);
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await engine.ProbeAsync(source, cancellationToken: cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.ProbeAsync(source));
            }
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.False(source.IsDisposed);
        }
    }
}
