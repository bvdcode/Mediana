using System.Buffers.Binary;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class Mp4SampleAccessTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", Mp4TimestampMode.Media)]
        [InlineData("avc-aac.mp4", Mp4TimestampMode.LinearMovie)]
        [InlineData("avc-tail.mov", Mp4TimestampMode.Media)]
        [InlineData("avc-tail.mov", Mp4TimestampMode.LinearMovie)]
        public async Task ReverseAndSequentialReadsMatchEveryIndependentPacket(string file, Mp4TimestampMode mode) =>
            await Mp4SampleAccessTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file, mode);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MetadataDoesNotReadPayloadAndRandomReadsTouchOnlyTheRequestedRange(bool wide)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            if (wide)
            {
                data = Mp4TestData.Use64BitChunkOffsets(data);
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 13);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            source.Reads.Clear();
            Mp4SampleInfo last = reader.GetSampleInfo(0, 5);
            Assert.Empty(source.Reads);
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 5);
            Assert.All(source.Reads, range => Assert.InRange(range.Offset, last.Offset, last.Offset + last.Size - 1));
            Assert.Equal(last.Size, source.Reads.Sum(range => range.Length));
            Assert.Equal(last.PresentationTime, packet.PresentationTime);
            Assert.True(packet.IsKeyFrame);
            using MediaPacket next = (await reader.ReadPacketAsync())!;
            Assert.Equal(new MediaTimestamp(2048, new(1, 10240)), next.DecodeTime);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(2, 0)]
        [InlineData(0, -1)]
        [InlineData(0, 8)]
        [InlineData(1, 76)]
        public async Task InvalidIndicesDoNotReadOrAllocate(int stream, int sample)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            int reads = source.Reads.Count;
            long allocated = allocator.AllocatedBytes;
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetSampleInfo(stream, sample));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ReadPacketAtAsync(stream, sample).AsTask());
            Assert.Equal(reads, source.Reads.Count);
            Assert.Equal(allocated, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationDuringRandomPayloadReadCanRetryWithoutMovingSequentialPosition()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(), 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                long allocated = allocator.AllocatedBytes;
                using CancellationTokenSource cancellation = new();
                source.AfterRead = cancellation.Cancel;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadPacketAtAsync(0, 7, cancellation.Token).AsTask());
                Assert.Equal(allocated, allocator.AllocatedBytes);
                source.AfterRead = null;
                using MediaPacket retry = await reader.ReadPacketAtAsync(0, 7);
                Assert.Equal(reader.GetSampleInfo(0, 7).PresentationTime, retry.PresentationTime);
                using MediaPacket first = (await reader.ReadPacketAsync())!;
                Assert.Equal(0, first.StreamIndex);
                Assert.Equal(-4096, first.DecodeTime!.Value.Value);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PreCancelledOperationsDoNotReadOrAllocate()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            int reads = source.Reads.Count;
            long allocated = allocator.AllocatedBytes;
            CancellationToken cancelled = new(true);
            Assert.Throws<OperationCanceledException>(() => reader.GetSampleInfo(0, 7, cancelled));
            await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadPacketAtAsync(0, 7, cancelled).AsTask());
            Assert.Equal(reads, source.Reads.Count);
            Assert.Equal(allocated, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PacketLeaseSurvivesReaderDisposalButNewSampleAccessFails()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            MediaPacket packet = await reader.ReadPacketAtAsync(1, 75);
            byte[] data = packet.Data.ToArray();
            await reader.DisposeAsync();
            Assert.Equal(data, packet.Data.ToArray());
            Assert.Equal(packet.Data.Length, allocator.AllocatedBytes);
            Assert.Throws<ObjectDisposedException>(() => reader.GetSampleInfo(0, 0));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => reader.ReadPacketAtAsync(0, 0).AsTask());
            packet.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task SignedCompositionOffsetsMatchSequentialValuesAtEveryIndex()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            int composition = Mp4TestData.PayloadOffset(data, "ctts");
            data[composition] = 1;
            int count = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(composition + 4));
            for (int index = 0; index < count; index++)
            {
                int position = composition + 12 + index * 8;
                int original = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(position), original - 8192);
            }
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            int[] indices = new int[2];
            while (await reader.ReadPacketAsync() is MediaPacket packet)
            {
                using (packet)
                {
                    Mp4SampleInfo info = reader.GetSampleInfo(packet.StreamIndex, indices[packet.StreamIndex]++);
                    Assert.Equal(packet.PresentationTime, info.PresentationTime);
                    Assert.Equal(packet.DecodeTime, info.DecodeTime);
                    Assert.Equal(packet.Duration, info.Duration);
                }
            }
            Assert.Equal(-4096, reader.GetSampleInfo(0, 0).PresentationTime.Value);
        }

        [Fact]
        public async Task AllocationFailureLeavesAllCursorsAndSourceUntouched()
        {
            byte[] data = await Mp4TestData.ReadAsync();
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using WritableMediaBuffer held = allocator.Allocate((int)(allocator.LimitBytes - allocator.AllocatedBytes));
            int reads = source.Reads.Count;
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => reader.ReadPacketAtAsync(0, 7).AsTask());
            Assert.Equal(reads, source.Reads.Count);
            held.Dispose();
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(0, first.DecodeTime!.Value.Value);
        }

        [Fact]
        public async Task ConstantSizeSamplesUseExactOffsetsInsideTheirChunk()
        {
            byte[] data = Mp4SampleTableTestData.WithConstantSampleSize(await Mp4TestData.ReadAsync("avc-tail.mov"), 3);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            int start = Mp4TestData.PayloadOffset(data, "mdat");
            for (int index = 5; index >= 0; index--)
            {
                Mp4SampleInfo info = reader.GetSampleInfo(0, index);
                Assert.Equal(start + index * 3, info.Offset);
                Assert.Equal(3, info.Size);
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, index);
                Assert.Equal(data.AsSpan(start + index * 3, 3).ToArray(), packet.Data.ToArray());
            }
        }

        [Fact]
        public async Task ConcurrentRejectionsDoNotInterruptTheActiveRandomRead()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(), 13);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            List<Task> rejected = [];
            source.AfterRead = () =>
            {
                source.AfterRead = null;
                Assert.Throws<InvalidOperationException>(() => reader.GetSampleInfo(0, 0));
                Assert.Throws<InvalidOperationException>(() => reader.DisposeAsync());
                rejected.Add(reader.ReadPacketAtAsync(0, 0).AsTask());
                rejected.Add(reader.ReadPacketAsync().AsTask());
            };
            using MediaPacket packet = await reader.ReadPacketAtAsync(0, 7);
            Assert.Equal(reader.GetSampleInfo(0, 7).PresentationTime, packet.PresentationTime);
            Assert.Equal(2, rejected.Count);
            foreach (Task task in rejected)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            }
            using MediaPacket first = (await reader.ReadPacketAsync())!;
            Assert.Equal(0, first.DecodeTime!.Value.Value);
        }

        [Fact]
        public async Task IoFailureReleasesRandomPacketStorageAndAllowsRetry()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync(), 13);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                long allocated = allocator.AllocatedBytes;
                source.AfterRead = () => throw new IOException("The input read failed.");
                await Assert.ThrowsAsync<IOException>(() => reader.ReadPacketAtAsync(1, 75).AsTask());
                Assert.Equal(allocated, allocator.AllocatedBytes);
                source.AfterRead = null;
                using MediaPacket packet = await reader.ReadPacketAtAsync(1, 75);
                Assert.Equal(reader.GetSampleInfo(1, 75).PresentationTime, packet.PresentationTime);
                using MediaPacket first = (await reader.ReadPacketAsync())!;
                Assert.Equal(0, first.DecodeTime!.Value.Value);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
