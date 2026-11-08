// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Ac3;
using Mediana.Codecs.Png;
using Mediana.Containers.Ac3;
using Mediana.Core;
using Mediana.Core.Metadata;
using Mediana.Formats.Audio;

namespace Mediana.Tests
{
    public class PacketMetadataTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RegisteredPrefixMetadataUsesTheCommonProbeAndOwnsItsSnapshot(bool images)
        {
            byte[] frame = First();
            FixturePacketMetadataReader metadata = new("ac3", 8, new AudioStreamMetadata(48000, 1));
            MediaEngineBuilder builder = new MediaEngineBuilder().AddContainer(new Ac3ReaderFactory()).AddStreamMetadataReader(metadata);
            if (images) { builder.AddImageDecoder(new PngImageDecoder()); }
            await using EbmlElementTestSource source = new(frame) { MaximumRead = 3 };
            if (!images) { source.ExcludedReadRange = (8, frame.Length); }
            MediaInfo info = await builder.Build().ProbeAsync(source, images ? null : new() { BufferBudgetBytes = 16, MaxProbeBytes = 8 });
            Assert.Equal(frame[..8], metadata.Prefix);
            Assert.Equal(1, metadata.PrefixReads);
            Assert.Equal(48000, Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]).SampleRate);
            Assert.False(source.IsDisposed);
            if (!images) { Assert.Equal(24, source.BytesRead); }
            await source.DisposeAsync();
            Array.Clear(frame);
            Assert.Equal(48000, Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]).SampleRate);
        }

        [Fact]
        public async Task ConfigurationMetadataAvoidsPacketInspection()
        {
            byte[] frame = First();
            AudioStreamMetadata expected = new(44100, 2);
            FixturePacketMetadataReader metadata = new("ac3", 8, null) { ConfigurationMetadata = expected };
            await using EbmlElementTestSource source = new(frame) { ExcludedReadRange = (8, frame.Length) };
            MediaInfo info = await Engine(metadata).ProbeAsync(source);
            Assert.Same(expected, info.StreamMetadata[0]);
            Assert.Equal(0, metadata.PrefixReads);
            Assert.Equal(16, source.BytesRead);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(9)]
        public async Task InvalidOrOverBudgetProbeLengthsAreExplicitFailures(int length)
        {
            FixturePacketMetadataReader metadata = new("ac3", length, null);
            await using EbmlElementTestSource source = new(First());
            if (length <= 0)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await Engine(metadata).ProbeAsync(source, new() { MaxProbeBytes = 8 }));
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(async () =>
                    await Engine(metadata).ProbeAsync(source, new() { MaxProbeBytes = 8 }));
            }
            Assert.Equal(0, metadata.PrefixReads);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task PrefixReadsRespectTheCompletePacketExtent()
        {
            byte[] frame = First();
            FixturePacketMetadataReader metadata = new("ac3", frame.Length + 11, null);
            await using EbmlElementTestSource source = new(frame);
            Assert.Empty((await Engine(metadata).ProbeAsync(source)).StreamMetadata);
            Assert.Equal(frame, metadata.Prefix);
        }

        [Fact]
        public async Task PrefixMetadataCannotChangeMediaKindAndCancellationDoesNotInspectPackets()
        {
            await using EbmlElementTestSource source = new(First());
            FixturePacketMetadataReader wrongKind = new("ac3", 8, new FixtureStreamMetadata(MediaKind.Video));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Engine(wrongKind).ProbeAsync(source));
            using CancellationTokenSource cancellation = new();
            FixturePacketMetadataReader cancelled = new("ac3", 8, null) { OnConfiguration = cancellation.Cancel };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Engine(cancelled).ProbeAsync(source, cancellationToken: cancellation.Token));
            Assert.Equal(0, cancelled.PrefixReads);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData("mono.ac3", false, 1)]
        [InlineData("stereo.eac3", true, 2)]
        public async Task Ac3MetadataDescribesSelfContainedHeadersWithoutCreatingDecoders(string name, bool enhanced, int channels)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", name));
            Ac3FrameHeader header = Ac3FrameHeader.Read(input);
            await using EbmlElementTestSource source = new(input[..header.FrameBytes]) { ExcludedReadRange = (8, header.FrameBytes) };
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Ac3ReaderFactory())
                .AddStreamMetadataReader(new Ac3StreamMetadataReader()).AddStreamMetadataReader(new Eac3StreamMetadataReader()).Build();
            MediaInfo info = await engine.ProbeAsync(source);
            AudioStreamMetadata metadata = Assert.IsType<AudioStreamMetadata>(info.StreamMetadata[0]);
            Assert.Equal(channels, metadata.ChannelCount);
            Assert.Equal(header.SampleRate, metadata.SampleRate);
            Assert.Equal(enhanced ? "eac3" : "ac3", info.Streams[0].CodecId);
            Assert.Empty(engine.AudioDecoderImplementations);
            Assert.Equal(24, source.BytesRead);
        }

        private static MediaEngine Engine(FixturePacketMetadataReader metadata) => new MediaEngineBuilder()
            .AddContainer(new Ac3ReaderFactory()).AddStreamMetadataReader(metadata).Build();
        private static byte[] First()
        {
            byte[] source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "Ac3", "mono.ac3"));
            return source[..Ac3FrameHeader.Read(source).FrameBytes];
        }
    }
}
