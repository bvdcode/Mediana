// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class EncodingSourceTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task LookaheadAndMultiplePacketsPerFrameDrainWithoutOwningTheEncoder(bool overlap)
        {
            MediaBufferAllocator allocator = new(128);
            using BufferedVideoEncoder encoder = new(allocator);
            int frames = 0;
            await using MediaEncodeSource<VideoFrame> source = new(ReadAsync, encoder, overlap);
            List<byte> output = [];
            while (true)
            {
                using MediaPacket? packet = await source.ReadPacketAsync();
                if (packet is null)
                {
                    break;
                }
                output.Add(packet.Data.Span[0]);
            }
            Assert.Equal(new byte[] { 0, 1, 1, 2, 2, 3 }, output);
            Assert.Null(await source.ReadPacketAsync());
            await source.DisposeAsync();
            Assert.False(encoder.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);

            ValueTask<VideoFrame?> ReadAsync(CancellationToken token) => ValueTask.FromResult<VideoFrame?>(
                frames == 3 ? null : FrameFixture.Create(allocator, (byte)frames, frames++));
        }

        [Fact]
        public async Task EncoderFailureCancelsAnOutstandingReadAndReleasesItsFrame()
        {
            MediaBufferAllocator allocator = new(128);
            TaskCompletionSource preparing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using BufferedVideoEncoder encoder = new(allocator)
            {
                Sending = token =>
                {
                    preparing.Task.Wait(TimeSpan.FromSeconds(10), token);
                    throw new InvalidDataException("Encoder failed.");
                }
            };
            int reads = 0;
            await using MediaEncodeSource<VideoFrame> source = new(ReadAsync, encoder, true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadPacketAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadPacketAsync());

            async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                if (++reads == 1) { return FrameFixture.Create(allocator, 7); }
                preparing.SetResult();
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                throw new TimeoutException();
            }
        }

        [Fact]
        public async Task PreparationFailureJoinsTheCancelledEncoder()
        {
            MediaBufferAllocator allocator = new(128);
            TaskCompletionSource sending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool finished = false;
            using BufferedVideoEncoder encoder = new(allocator)
            {
                Sending = token =>
                {
                    sending.SetResult();
                    try
                    {
                        if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
                        token.ThrowIfCancellationRequested();
                    }
                    finally { finished = true; }
                }
            };
            int reads = 0;
            await using MediaEncodeSource<VideoFrame> source = new(ReadAsync, encoder, true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadPacketAsync());
            Assert.True(finished);
            Assert.Equal(0, allocator.AllocatedBytes);

            async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                if (++reads == 1) { return FrameFixture.Create(allocator, 7); }
                await sending.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                throw new InvalidDataException("Preparation failed.");
            }
        }

        [Fact]
        public async Task EarlyDisposalReleasesThePreparedFrameAndLeavesTheEncoderOwnedByItsCaller()
        {
            MediaBufferAllocator allocator = new(128);
            using BufferedVideoEncoder encoder = new(allocator);
            int reads = 0;
            await using MediaEncodeSource<VideoFrame> source = new(_ =>
                ValueTask.FromResult<VideoFrame?>(FrameFixture.Create(allocator, 7, reads++)), encoder, true);
            using (MediaPacket? packet = await source.ReadPacketAsync()) { Assert.NotNull(packet); }
            Assert.Equal(3, reads);
            await source.DisposeAsync();
            Assert.False(encoder.IsDisposed);
            encoder.Reset();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationReleasesAFramePreparedWhileTheEncoderIsBusy()
        {
            MediaBufferAllocator allocator = new(128);
            using CancellationTokenSource cancellation = new();
            using BufferedVideoEncoder encoder = new(allocator)
            {
                Sending = token =>
                {
                    if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) { throw new TimeoutException(); }
                    token.ThrowIfCancellationRequested();
                }
            };
            int reads = 0;
            await using MediaEncodeSource<VideoFrame> source = new(ReadAsync, encoder, true);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.ReadPacketAsync(cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);

            async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                VideoFrame frame = FrameFixture.Create(allocator, 7, reads++);
                if (reads == 2) { await cancellation.CancelAsync(); }
                return frame;
            }
        }

        [Fact]
        public async Task InvalidDrainIsTerminalAndDoesNotDisposeBorrowedEncoder()
        {
            MediaBufferAllocator allocator = new(128);
            using BufferedVideoEncoder encoder = new(allocator, invalidDrain: true);
            await using MediaEncodeSource<VideoFrame> source = new(_ => ValueTask.FromResult<VideoFrame?>(null), encoder);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadPacketAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadPacketAsync());
            Assert.False(encoder.IsDisposed);
        }

        [Fact]
        public async Task CancellationAfterFrameTransferReleasesCallbackOwnership()
        {
            MediaBufferAllocator allocator = new(128);
            using BufferedVideoEncoder encoder = new(allocator);
            using CancellationTokenSource cancellation = new();
            await using MediaEncodeSource<VideoFrame> source = new(ReadAsync, encoder);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await source.ReadPacketAsync(cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadPacketAsync());

            async ValueTask<VideoFrame?> ReadAsync(CancellationToken token)
            {
                VideoFrame frame = FrameFixture.Create(allocator, 1);
                await cancellation.CancelAsync();
                return frame;
            }
        }
    }
}
