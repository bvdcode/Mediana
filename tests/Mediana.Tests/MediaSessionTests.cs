// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Pcm;
using Mediana.Core;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class MediaSessionTests
    {
        [Fact]
        public async Task RealMultiplexedAudioAndVideoMatchIndependentDecoding() =>
            await MultiStreamTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4", "avc-aac.mp4"));

        [Fact]
        public async Task SequentialSourceIsDemuxedOnceAndAllTracksDrainWithoutReadAhead()
        {
            InterleavedContainerFactory factory = new();
            FixtureDecoderFactory video = new();
            MediaEngine engine = Engine(factory, video);
            byte[] bytes = Enumerable.Range(10, 20).Select(value => (byte)value).ToArray();
            await using ShortReadSource source = new(bytes);
            await using MediaSession session = await engine.OpenMediaAsync(source);
            List<byte> pictures = [];
            List<byte> samples = [];
            DecodedMediaFrame? retained = null;
            while (await session.ReadFrameAsync() is DecodedMediaFrame frame)
            {
                int before = factory.Reader!.Reads;
                await Task.Yield();
                Assert.Equal(before, factory.Reader.Reads);
                switch (frame.Kind)
                {
                    case MediaKind.Video:
                        Assert.Equal(3, frame.StreamIndex);
                        pictures.Add(frame.Video!.Data.Span[0]);
                        Assert.Null(frame.Audio);
                        break;
                    case MediaKind.Audio:
                        Assert.Equal(7, frame.StreamIndex);
                        samples.Add(frame.Audio!.GetPlane(0).Span[0]);
                        Assert.Null(frame.Video);
                        break;
                    default:
                        throw new InvalidOperationException("Unexpected decoded media kind.");
                }
                retained?.Dispose();
                retained = frame;
            }
            Assert.Equal(bytes.Where((_, index) => index % 2 == 0), pictures);
            Assert.Equal(bytes.Where((_, index) => index % 2 != 0), samples);
            Assert.Equal(1, factory.Opens);
            Assert.Equal(bytes.Length + 1, factory.Reader!.Reads);
            Assert.Null(await session.ReadFrameAsync());
            await session.DisposeAsync();
            Assert.True(video.LastDecoder!.IsDisposed);
            Assert.True(factory.Reader.IsDisposed);
            Assert.False(source.IsDisposed);
            Assert.NotNull(retained);
            Assert.NotEmpty(retained.Video!.Data.ToArray());
            retained.Dispose();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task ExplicitSelectionCreatesOnlyRequestedDecoders()
        {
            InterleavedContainerFactory factory = new();
            await using ShortReadSource source = new([10, 11, 12, 13]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddAudioDecoder(new PcmDecoderFactory()).Build();
            await using MediaSession session = await engine.OpenMediaAsync(source, [7]);
            Assert.Single(session.Streams);
            List<byte> values = [];
            while (await session.ReadFrameAsync() is DecodedMediaFrame frame)
            {
                using (frame)
                {
                    Assert.Equal(7, frame.StreamIndex);
                    values.Add(frame.Audio!.GetPlane(0).Span[0]);
                }
            }
            Assert.Equal(new byte[] { 11, 13 }, values);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SharedReadLimitsReleaseAllDecoderState(bool packetLimit)
        {
            InterleavedContainerFactory factory = new();
            FixtureDecoderFactory decoder = new();
            await using ShortReadSource source = new([10, 11, 12, 13]);
            MediaOpenOptions options = packetLimit ? new() { MaxPacketReads = 1 } : new() { MaxFrameReads = 1 };
            await using MediaSession session = await Engine(factory, decoder).OpenMediaAsync(source, options: options);
            if (!packetLimit)
            {
                using DecodedMediaFrame? first = await session.ReadFrameAsync();
                Assert.NotNull(first);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
            Assert.True(decoder.LastDecoder!.IsDisposed);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task AwaitedInputRejectsConcurrentOperationsAndCancellationCleansEveryTrack()
        {
            InterleavedContainerFactory factory = new();
            FixtureDecoderFactory decoder = new();
            await using ShortReadSource source = new([10, 11, 12, 13]);
            await using MediaSession session = await Engine(factory, decoder).OpenMediaAsync(source);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            factory.Reader!.BeforeRead = async token =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            using CancellationTokenSource cancellation = new();
            Task<DecodedMediaFrame?> pending = session.ReadFrameAsync(cancellation.Token).AsTask();
            await entered.Task;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ReadFrameAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(decoder.LastDecoder!.IsDisposed);
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task FailedTrackCreationDisposesEarlierDecodersAndReader()
        {
            InterleavedContainerFactory factory = new();
            FixtureDecoderFactory decoder = new();
            await using ShortReadSource source = new([10, 11]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(factory).AddVideoDecoder(decoder).Build();
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.OpenMediaAsync(source));
            Assert.True(decoder.LastDecoder!.IsDisposed);
            Assert.True(factory.Reader!.IsDisposed);
            Assert.Equal(0, decoder.Allocator!.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        private static MediaEngine Engine(InterleavedContainerFactory container, FixtureDecoderFactory video) =>
            new MediaEngineBuilder().AddContainer(container).AddVideoDecoder(video).AddAudioDecoder(new PcmDecoderFactory()).Build();
    }
}
