using Mediana.Containers.MpegTs;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class MpegTsWriterTests
    {
        [Theory]
        [InlineData("avc-aac.mp4")]
        [InlineData("avc-tail.mov")]
        [InlineData("avc-cabac-8x8-2.mp4")]
        public async Task RealAvcAacRemuxPreservesEveryAccessUnitAndPtsDts(string file) =>
            await MpegTsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file);

        [Theory]
        [InlineData("avc-aac.mp4")]
        [InlineData("avc-tail.mov")]
        [InlineData("avc-cabac-8x8-2.mp4")]
        public async Task RealMovieRemuxPreservesTranslatedDecodeOrderAndPresentationTimes(string file) =>
            await MpegTsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file,
                Mp4TimestampMode.LinearMovie);

        [Fact]
        public async Task AudioOnlyProgramFillsClockGapsAndReleasesAllStorage()
        {
            MediaBufferAllocator allocator = new(1024);
            using MemoryStream destination = new();
            EncodedStreamInfo stream = Audio();
            List<(int Stream, byte[] Data, long Pts, long Dts)> expected = [];
            await using (MpegTsWriter writer = MpegTsWriter.Create(destination, [stream], allocator))
            {
                foreach (int milliseconds in new[] { 0, 1000 })
                {
                    using MediaPacket packet = Packet(allocator, milliseconds);
                    await writer.WritePacketAsync(packet);
                    expected.Add((7, [1, 2, 3], 90000 + milliseconds * 90, 90000 + milliseconds * 90));
                }
                await writer.CompleteAsync();
                await writer.CompleteAsync();
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            MpegTsTestInput.Verify(destination.ToArray(), [stream], expected);
            Assert.True(destination.CanWrite);
        }

        [Fact]
        public async Task MissingOrUnorderedTimestampsFaultWriterAndReleaseWorkers()
        {
            MediaBufferAllocator allocator = new(1024);
            using MemoryStream destination = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator);
            using MediaPacket packet = Packet(allocator, 10);
            await writer.WritePacketAsync(packet);
            long retained = allocator.AllocatedBytes - 207;
            await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(packet).AsTask());
            Assert.Equal(retained, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
        }

        [Fact]
        public async Task CancellationBeforeOutputFaultsWriterWithoutRetainingPacket()
        {
            MediaBufferAllocator allocator = new(1024);
            using MemoryStream destination = new();
            await using MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator);
            using MediaPacket packet = Packet(allocator, 0);
            await Assert.ThrowsAsync<OperationCanceledException>(() => writer.WritePacketAsync(packet, new CancellationToken(true)).AsTask());
            Assert.Empty(destination.ToArray());
            Assert.Equal(3, allocator.AllocatedBytes);
            Assert.Equal(new byte[] { 1, 2, 3 }, packet.Data.ToArray());
        }

        [Fact]
        public async Task EmptyCompletionAndUnsupportedConfigurationWriteNothing()
        {
            MediaBufferAllocator allocator = new(1024);
            using MemoryStream destination = new();
            Assert.Throws<NotSupportedException>(() => MpegTsWriter.Create(destination,
                [new EncodedStreamInfo(0, MediaKind.Audio, "opus", new TimeBase(1, 48000))], allocator));
            Assert.Throws<MediaMemoryLimitException>(() => MpegTsWriter.Create(destination, [Audio()], new(206)));
            Assert.Equal(0, allocator.AllocatedBytes);
            await using MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Empty(destination.ToArray());
        }

        private static EncodedStreamInfo Audio() => new(7, MediaKind.Audio, "aac", new TimeBase(1, 1000), [0x11, 0x88]);

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task PartialIoFailureFaultsWriterReleasesStorageAndKeepsBorrowedOutput(int write)
        {
            MediaBufferAllocator allocator = new(1024);
            using MpegTsTestOutput destination = new() { FailOnWrite = write };
            await using MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator);
            using MediaPacket packet = Packet(allocator, 0);
            if (write == 4)
            {
                await writer.WritePacketAsync(packet);
                using MediaPacket next = Packet(allocator, 1000);
                await Assert.ThrowsAsync<IOException>(() => writer.WritePacketAsync(next).AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => writer.WritePacketAsync(packet).AsTask());
            }
            Assert.Equal(3, allocator.AllocatedBytes);
            Assert.False(destination.WasDisposed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            Assert.Equal(17, destination.Bytes.Length % 188);
        }

        [Fact]
        public async Task ConcurrentOperationsAreRejectedWithoutInterruptingTheActiveWrite()
        {
            MediaBufferAllocator allocator = new(1024);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using MpegTsTestOutput destination = new() { Gate = gate };
            await using MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator);
            using MediaPacket packet = Packet(allocator, 0);
            Task pending = writer.WritePacketAsync(packet).AsTask();
            await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WritePacketAsync(packet).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            Assert.Throws<InvalidOperationException>(() => writer.DisposeAsync());
            Assert.Throws<InvalidOperationException>(writer.CaptureSegmentState);
            gate.SetResult();
            await pending;
            await writer.CompleteAsync();
            Assert.Equal(3, allocator.AllocatedBytes);
            Assert.False(destination.WasDisposed);
        }

        [Fact]
        public async Task InFlightCancellationAndFramingAllocationFailureReleaseEveryWorker()
        {
            MediaBufferAllocator allocator = new(1024);
            using MpegTsTestOutput destination = new() { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            await using (MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator))
            using (MediaPacket packet = Packet(allocator, 0))
            using (CancellationTokenSource cancellation = new())
            {
                Task pending = writer.WritePacketAsync(packet, cancellation.Token).AsTask();
                await destination.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.Equal(3, allocator.AllocatedBytes);
                Assert.Equal(0, destination.Bytes.Length);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator small = new(210);
            using MemoryStream output = new();
            await using (MpegTsWriter writer = MpegTsWriter.Create(output, [Audio()], small))
            using (MediaPacket packet = Packet(small, 0))
            {
                await Assert.ThrowsAsync<MediaMemoryLimitException>(() => writer.WritePacketAsync(packet).AsTask());
                Assert.Equal(3, small.AllocatedBytes);
                Assert.Equal(0, output.Length);
            }
            Assert.Equal(0, small.AllocatedBytes);
        }

        [Fact]
        public async Task FailedCompletionCannotBeRetriedAsSuccessfulOutput()
        {
            MediaBufferAllocator allocator = new(1024);
            using MpegTsTestOutput destination = new() { FailOnFlush = true };
            await using MpegTsWriter writer = MpegTsWriter.Create(destination, [Audio()], allocator);
            using (MediaPacket packet = Packet(allocator, 0))
            {
                await writer.WritePacketAsync(packet);
            }
            await Assert.ThrowsAsync<IOException>(() => writer.CompleteAsync().AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            Assert.False(destination.WasDisposed);
        }

        private static MediaPacket Packet(MediaBufferAllocator allocator, int milliseconds)
        {
            using WritableMediaBuffer storage = allocator.Allocate(3);
            new byte[] { 1, 2, 3 }.CopyTo(storage.Memory);
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp timestamp = new(milliseconds, new TimeBase(1, 1000));
            return new(7, buffer, timestamp, timestamp);
        }
    }
}
