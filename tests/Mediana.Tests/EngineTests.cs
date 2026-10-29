// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class EngineTests
    {
        [Fact]
        public async Task RandomAccessOpeningUsesOriginWithoutChangingCallerCursor()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory())
                .AddVideoDecoder(new FixtureDecoderFactory()).Build();
            using MemoryStream stream = new([.. "TEST"u8, 11, 22]);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            Assert.Equal(1, await source.ReadAsync(new byte[1]));
            await using (VideoSession session = await engine.OpenVideoAsync(source, 0))
            {
                using VideoFrame? frame = await session.ReadFrameAsync();
                Assert.NotNull(frame);
                Assert.Equal(11, frame.Data.Span[0]);
                Assert.Equal(1, stream.Position);
            }
            Assert.True(stream.CanRead);
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public async Task NonSeekableShortReadsReplayProbeAndDrainDelayedFrames()
        {
            FixtureContainerFactory container = new();
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(container).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22, 33]);
            VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame? first = await session.ReadFrameAsync();
            Assert.NotNull(first);
            Assert.Equal(11, first.Data.Span[0]);
            Assert.Equal(1, decoder.LastDecoder!.PendingCount);
            using VideoFrame? last = await session.ReadFrameAtOrAfterAsync(new MediaTimestamp(2, new TimeBase(1, 1)));
            Assert.NotNull(last);
            Assert.Equal(33, last.Data.Span[0]);
            Assert.Equal(2, last.Timestamp!.Value.Value);
            Assert.Null(await session.ReadFrameAsync());
            await session.DisposeAsync();
            await session.DisposeAsync();
            Assert.True(container.LastReader!.IsDisposed);
            Assert.True(decoder.LastDecoder.IsDisposed);
            Assert.False(source.IsDisposed);
            Assert.Equal(8, decoder.Allocator!.AllocatedBytes);
            Assert.Equal(11, first.Data.Span[0]);
            first.Dispose();
            last.Dispose();
            Assert.Equal(0, decoder.Allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationReleasesDecoderReferences()
        {
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new FixtureContainerFactory()).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 1, 2, 3]);
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame? first = await session.ReadFrameAsync();
            Assert.NotNull(first);
            first.Dispose();
            Assert.Equal(1, decoder.LastDecoder!.PendingCount);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await session.ReadFrameAsync(new CancellationToken(true)));
            Assert.True(decoder.LastDecoder.IsDisposed);
            Assert.Equal(0, decoder.Allocator!.AllocatedBytes);
            await session.DisposeAsync();
            Assert.Equal(0, decoder.Allocator!.AllocatedBytes);
        }

        [Fact]
        public async Task PacketLimitStopsScanAndReleasesRetainedInput()
        {
            FixtureDecoderFactory decoder = new();
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new FixtureContainerFactory()).AddVideoDecoder(decoder).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 1, 2, 3]);
            await using VideoSession session = await engine.OpenVideoAsync(source, 0,
                new MediaOpenOptions { MaxPacketReads = 1 });
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
            Assert.True(decoder.LastDecoder!.IsDisposed);
            Assert.Equal(0, decoder.Allocator!.AllocatedBytes);
        }

        [Fact]
        public async Task OpenFailureDisposesReaderButLeavesCallerSourceOpen()
        {
            FixtureContainerFactory container = new();
            MediaEngine engine = new MediaEngineBuilder().AddContainer(container)
                .AddVideoDecoder(new FixtureDecoderFactory(rejection: "Unsupported profile")).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 1]);
            NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(
                async () => await engine.OpenVideoAsync(source, 0));
            Assert.Contains("Unsupported profile", error.Message);
            Assert.True(container.LastReader!.IsDisposed);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task UnsupportedInputAndAmbiguousRegistrationsAreExplicit()
        {
            await using ShortReadSource unknown = new("WHAT"u8.ToArray());
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.OpenVideoAsync(unknown, 0));

            MediaEngine ambiguous = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory("first"))
                .AddContainer(new FixtureContainerFactory("second")).Build();
            await using ShortReadSource source = new("TEST"u8.ToArray());
            NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(
                async () => await ambiguous.OpenVideoAsync(source, 0));
            Assert.Contains("Multiple", error.Message);
        }

        [Fact]
        public void RegistryRejectsDuplicateIdsAndBuildIsASnapshot()
        {
            MediaEngineBuilder builder = new();
            MediaEngine empty = builder.Build();
            builder.AddContainer(new FixtureContainerFactory()).AddVideoDecoder(new FixtureDecoderFactory());
            Assert.Empty(empty.ContainerImplementations);
            Assert.Empty(empty.VideoDecoderImplementations);
            Assert.Throws<ArgumentException>(() => builder.AddContainer(new FixtureContainerFactory()));
            Assert.Throws<ArgumentException>(() => builder.AddVideoDecoder(new FixtureDecoderFactory()));
        }

        [Fact]
        public async Task ProbeAndFrameLimitsAreEnforced()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory())
                .AddVideoDecoder(new FixtureDecoderFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 1, 2, 3]);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.OpenVideoAsync(source, 0,
                new MediaOpenOptions { MaxProbeBytes = 3 }));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0,
                new MediaOpenOptions { MaxFrameReads = 1 });
            using VideoFrame? first = await session.ReadFrameAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
        }

        [Fact]
        public async Task EndOfStreamDoesNotSubstituteAnEarlierFrame()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory())
                .AddVideoDecoder(new FixtureDecoderFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 1, 2]);
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            Assert.Null(await session.ReadFrameAtOrAfterAsync(new MediaTimestamp(10, new TimeBase(1, 1))));
            Assert.Equal(0, session.AllocatedBufferBytes);
        }
    }
}
