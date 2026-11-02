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
        [Fact]
        public async Task LookaheadAndMultiplePacketsPerFrameDrainWithoutOwningTheEncoder()
        {
            MediaBufferAllocator allocator = new(128);
            using BufferedVideoEncoder encoder = new(allocator);
            int frames = 0;
            await using MediaEncodeSource<VideoFrame> source = new(ReadAsync, encoder);
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
