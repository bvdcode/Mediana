// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class VideoSeekTests
    {
        [Theory]
        [InlineData("avc-multislice-cavlc", 18, 96, 80, 6)]
        [InlineData("avc-multislice-cabac", 18, 96, 80, 6)]
        [InlineData("avc-multislice-cabac-sliced", 18, 96, 80, 6)]
        [InlineData("avc-segments", 36, 64, 48, 5)]
        [InlineData("avc-aac", 8, 64, 48, 5)]
        [InlineData("avc-weighted", 12, 94, 62, 10)]
        public async Task OutOfOrderSeeksEofRestartAndRetainedFramesMatchIndependentPictures(string name,
            int count, int width, int height, int frameRate) =>
            await VideoSeekTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), name, count, width, height, frameRate);

        [Fact]
        public async Task LateSeekReadsOnlyTheSelectedGopAndReusesItsInspectedPacket()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            byte[] bytes = await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4");
            await using TrackingRandomAccessSource metadataSource = new(bytes);
            await using Mp4Reader metadata = await Mp4Reader.OpenAsync(metadataSource, allocator);
            Mp4SampleInfo[] permitted = Enumerable.Range(12, 6).Select(index => metadata.GetSampleInfo(0, index)).ToArray();
            await using TrackingRandomAccessSource source = new(bytes, 13);
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0);
            source.Reads.Clear();
            MediaTimestamp target = new(2800, new(1, 1000));
            await session.SeekAsync(target);
            Assert.Equal(permitted[0].Size, source.Reads.Sum(range => range.Length));
            using VideoFrame frame = (await session.ReadFrameAtOrAfterAsync(target))!;
            Assert.Equal(new MediaTimestamp(17, new(1, 6)), frame.Timestamp);
            Assert.All(source.Reads, range => Assert.Contains(permitted,
                sample => range.Offset >= sample.Offset && range.Offset + range.Length <= sample.Offset + sample.Size));
            Assert.Equal(permitted.Sum(sample => sample.Size), source.Reads.Sum(range => range.Length));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PacketAndFrameBudgetsRemainCumulativeAcrossSeeks(bool frames)
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4"));
            MediaOpenOptions options = frames ? new() { MaxFrameReads = 1 } : new() { MaxPacketReads = 1 };
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0, options);
            MediaTimestamp target = new(0, new(1, 1));
            await session.SeekAsync(target);
            if (frames)
            {
                using VideoFrame frame = (await session.ReadFrameAsync())!;
                await session.SeekAsync(target);
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.SeekAsync(target).AsTask());
            }
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task CancellationDuringCandidateReadReleasesPendingPacketsAndMakesTheSessionTerminal()
        {
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4"), 11);
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0);
            using CancellationTokenSource cancellation = new();
            source.AfterRead = cancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SeekAsync(new(2, new(1, 1)), cancellation.Token).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task ContainerKeyFlagsCannotRestartTheDecoderAtAPredictivePicture()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4");
            int sync = Mp4TestData.PayloadOffset(data, "stss");
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(sync + 12), 14);
            await using TrackingRandomAccessSource source = new(data);
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0);
            MediaTimestamp target = new(2800, new(1, 1000));
            await session.SeekAsync(target);
            using VideoFrame frame = (await session.ReadFrameAtOrAfterAsync(target))!;
            byte[] expected = await Mp4TestData.ReadAsync("avc-multislice-cabac.sequence.yuv");
            H264TestInput.VerifyFrame(frame, expected.AsSpan(17 * 11520, 11520));
        }

        [Fact]
        public async Task UnsupportedSeekingIsExplicitAndAnInvalidTimestampDoesNotChangeTheSession()
        {
            MediaEngine sequentialEngine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory())
                .AddVideoDecoder(new FixtureDecoderFactory()).Build();
            await using ShortReadSource sequentialSource = new([.. "TEST"u8, 11, 22]);
            await using VideoSession sequential = await sequentialEngine.OpenVideoAsync(sequentialSource, 0);
            Assert.False(sequential.CanSeek);
            await Assert.ThrowsAsync<NotSupportedException>(() => sequential.SeekAsync(new(0, new(1, 1))).AsTask());
            await sequential.DisposeAsync();
            Assert.Equal(0, sequential.AllocatedBufferBytes);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4"));
            await using VideoSession indexed = await Engine().OpenVideoAsync(source, 0);
            await Assert.ThrowsAsync<ArgumentException>(() => indexed.SeekAsync(default).AsTask());
            await indexed.SeekAsync(new(0, new(1, 1)));
            using VideoFrame first = (await indexed.ReadFrameAsync())!;
            Assert.Equal(new MediaTimestamp(0, new(1, 1)), first.Timestamp);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AwaitedSeekRejectsConcurrentOperationsAndSupportsCancellation(bool cancel)
        {
            await using BlockingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-multislice-cabac.mp4"));
            await using VideoSession session = await Engine().OpenVideoAsync(source, 0);
            source.BlockReads = true;
            using CancellationTokenSource cancellation = new();
            Task seek = session.SeekAsync(new(2, new(1, 1)), cancellation.Token).AsTask();
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SeekAsync(new(0, new(1, 1))).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            if (cancel)
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seek);
            }
            else
            {
                source.Resume.TrySetResult();
                await seek;
                using VideoFrame frame = (await session.ReadFrameAsync())!;
                Assert.Equal(new MediaTimestamp(2, new(1, 1)), frame.Timestamp);
            }
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        private static MediaEngine Engine() => new MediaEngineBuilder()
            .AddContainer(new Mp4ReaderFactory(new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie }))
            .AddVideoDecoder(new H264DecoderFactory()).Build();
    }
}
