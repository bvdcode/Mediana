// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class ContainerStreamInfoTests
    {
        [Fact]
        public void HeaderDeclarationsAndIndexTotalsRemainDistinctAndImmutable()
        {
            MediaTag[] tags = [new("mdta", "title", "Audio")];
            ContainerPacketIndexInfo index = new(17, 12345, new(17408, new(1, 48000)));
            ContainerStreamInfo stream = new(4, new(362, new(1, 1000)), tags, index, "en-GB");
            ContainerStreamInfo[] streams = [stream];
            ContainerInfo container = new("fixture", streamInfo: streams);
            tags[0] = new("mdta", "title", "Changed");
            streams[0] = new(3);
            Assert.Same(stream, container.StreamInfo![4]);
            Assert.Equal("Audio", stream.Tags![0].Values[0]);
            Assert.Equal(new MediaDuration(362, new(1, 1000)), stream.DeclaredDuration);
            Assert.Equal(new MediaDuration(17408, new(1, 48000)), stream.PacketIndex!.DecodeDuration);
            Assert.Equal(17, stream.PacketIndex.PacketCount);
            Assert.Equal(12345, stream.PacketIndex.PayloadBytes);
            Assert.Equal("en-GB", stream.Language);
            Assert.Throws<NotSupportedException>(() => ((IDictionary<int, ContainerStreamInfo>)container.StreamInfo).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<MediaTag>)stream.Tags).Clear());
        }

        [Fact]
        public void UnknownEmptyAndZeroMetadataRemainDistinct()
        {
            Assert.Null(new ContainerInfo("fixture").StreamInfo);
            Assert.Empty(new ContainerInfo("fixture", streamInfo: []).StreamInfo!);
            ContainerStreamInfo unknown = new(0);
            Assert.Null(unknown.DeclaredDuration);
            Assert.Null(unknown.PacketIndex);
            Assert.Null(unknown.Tags);
            Assert.Null(unknown.Language);
            ContainerStreamInfo zero = new(0, new(0, new(1, 1)), [], new(0, 0, new(0, new(1, 2))));
            Assert.Equal(0, zero.PacketIndex!.DecodeDuration.Value);
            Assert.Empty(zero.Tags!);
        }

        [Fact]
        public void InvalidMetadataDoesNotCreateAmbiguousIndicesOrCounters()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerStreamInfo(-1));
            Assert.Throws<ArgumentException>(() => new ContainerStreamInfo(0, language: " "));
            Assert.Throws<ArgumentException>(() => new ContainerStreamInfo(0, tags: [null!]));
            Assert.Throws<ArgumentException>(() => new ContainerInfo("fixture", streamInfo: [new(1), new(1)]));
            Assert.Throws<ArgumentNullException>(() => new ContainerInfo("fixture", streamInfo: [null!]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerPacketIndexInfo(-1, 0, new(0, new(1, 1))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerPacketIndexInfo(0, -1, new(0, new(1, 1))));
            Assert.Throws<ArgumentException>(() => new ContainerPacketIndexInfo(0, 0, default));
        }

        [Fact]
        public async Task ProbePreservesTrackScopeWithoutReadingPacketsAndMetadataSurvivesDisposal()
        {
            ContainerStreamInfo track = new(0, tags: [new("fixture", "title", "Track title")]);
            ContainerInfo container = new("fixture", tags: [new("fixture", "title", "Container title")], streamInfo: [track]);
            FixtureContainerFactory factory = new(info: container);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            MediaInfo result;
            await using (ShortReadSource source = new([.. "TEST"u8, 11]))
            {
                result = await engine.ProbeAsync(source);
                Assert.False(source.IsDisposed);
                Assert.True(factory.LastReader!.IsDisposed);
                Assert.Equal(0, factory.LastReader.PacketReads);
            }
            Assert.Same(track, result.Container!.StreamInfo![0]);
            Assert.Equal("Track title", result.Container.StreamInfo[0].Tags![0].Values[0]);
            Assert.Equal("Container title", result.Container.Tags![0].Values[0]);
        }

        [Fact]
        public async Task ForeignStreamMetadataIsRejectedAndTheReaderIsReleased()
        {
            FixtureContainerFactory factory = new(info: new("fixture", streamInfo: [new(1)]));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await engine.ProbeAsync(source));
            Assert.True(factory.LastReader!.IsDisposed);
            Assert.Equal(0, factory.LastReader.PacketReads);
            Assert.False(source.IsDisposed);
        }
    }
}
