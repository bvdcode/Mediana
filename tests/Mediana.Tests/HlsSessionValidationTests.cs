// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsSessionValidationTests
    {
        [Fact]
        public async Task InvalidOptionsAndIndicesOrCancellationBeforeOpeningReadNothing()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4HlsSession.OpenAsync(source, allocator, -1).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0, -1).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0, 0).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0,
                options: new() { Reading = new() { TimestampMode = Mp4TimestampMode.Media } }).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0,
                options: new() { MaximumSegmentUriCharacters = 0 }).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0,
                cancellationToken: new(true)).AsTask());
            Assert.Empty(source.Reads);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidRequestOrPreCancelledOutputDoesNotReadSource()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options());
            source.Reads.Clear();
            long retained = allocator.AllocatedBytes;
            using MemoryStream output = new();
            using MemoryStream readOnly = new([0], writable: false);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.WriteSegmentAsync(-1, output).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.WriteSegmentAsync(3, output).AsTask());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WriteSegmentAsync(0, output, new(true)).AsTask());
            await Assert.ThrowsAsync<ArgumentException>(() => session.WriteSegmentAsync(0, readOnly).AsTask());
            Assert.Empty(source.Reads);
            Assert.Equal(retained, allocator.AllocatedBytes);
            await session.DisposeAsync();
            await session.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.WriteSegmentAsync(0, output).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            Assert.Equal(0, output.Length);
        }

        [Fact]
        public async Task CancellationDuringPacketIndexingReleasesReaderAndAllowsReopen()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"), 19);
            long offset;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                offset = reader.GetSampleInfo(1, 10).Offset;
            }
            using CancellationTokenSource cancellation = new();
            source.AfterRead = () =>
            {
                if (source.Reads[^1].Offset == offset)
                {
                    cancellation.Cancel();
                }
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Mp4HlsSession.OpenAsync(source, allocator, 0, 1,
                HlsSessionTestInput.Options(), cancellation.Token).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
            source.AfterRead = null;
            await using (Mp4HlsSession session = await Mp4HlsSession.OpenAsync(source, allocator, 0, 1, HlsSessionTestInput.Options()))
            using (MemoryStream output = new())
            {
                await session.WriteSegmentAsync(2, output);
                Assert.NotEqual(0, output.Length);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
