using Mediana.Codecs.Mp3;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class Mp3BitReservoirTests
    {
        [Fact]
        public void MainDataCanCrossMultipleFramesWithoutHeadersOrSideInfo()
        {
            MediaBufferAllocator allocator = new(1024);
            using Mp3BitReservoir reservoir = new(allocator, MpegAudioVersion.Mpeg1, 16);
            Assert.Empty(reservoir.Assemble([10, 11, 12], 0, 0).ToArray());
            Assert.Empty(reservoir.Assemble([20, 21], 3, 0).ToArray());
            Assert.Equal(new byte[] { 11, 12, 20, 21, 30, 31 }, reservoir.Assemble([30, 31, 32, 33], 4, 48).ToArray());
            Assert.Equal(2, reservoir.RetainedBytes);
            Assert.Equal(new byte[] { 32, 33, 40 }, reservoir.Assemble([40, 41], 2, 24).ToArray());
            Assert.Equal(1, reservoir.RetainedBytes);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(8)]
        public void NextFrameCannotReuseAnyBitsOfTheLastConsumedByte(int bits)
        {
            MediaBufferAllocator allocator = new(1024);
            using Mp3BitReservoir reservoir = new(allocator, MpegAudioVersion.Mpeg1, 16);
            Assert.Equal(new byte[] { 0xa5 }, reservoir.Assemble([0xa5, 0x42, 0x21], 0, bits).ToArray());
            Assert.Equal(2, reservoir.RetainedBytes);
            Assert.Throws<InvalidDataException>(() => reservoir.Assemble([0x64], 3, 8).ToArray());
            Assert.Throws<InvalidOperationException>(() => reservoir.Assemble([1], 0, 8).ToArray());
            reservoir.Reset();
            Assert.Equal(new byte[] { 9 }, reservoir.Assemble([9], 0, 8).ToArray());
        }

        [Theory]
        [InlineData(MpegAudioVersion.Mpeg1, 511)]
        [InlineData(MpegAudioVersion.Mpeg2, 255)]
        [InlineData(MpegAudioVersion.Mpeg25, 255)]
        public void RetentionIsBoundedByTheVersionBackPointer(MpegAudioVersion version, int limit)
        {
            MediaBufferAllocator allocator = new(4096);
            using Mp3BitReservoir reservoir = new(allocator, version, 1024);
            byte[] bytes = Enumerable.Range(0, 1024).Select(value => (byte)(value % 251)).ToArray();
            reservoir.Assemble(bytes, 0, 0);
            Assert.Equal(limit, reservoir.RetainedBytes);
            Assert.Equal(bytes.AsSpan(bytes.Length - limit).ToArray(), reservoir.Assemble([], limit, limit * 8).ToArray());
            Assert.Equal(0, reservoir.RetainedBytes);
            Assert.Equal(1024 + limit, allocator.AllocatedBytes);
            reservoir.Reset();
            Assert.Throws<InvalidDataException>(() => reservoir.Assemble([], limit + 1, 0).ToArray());
        }

        [Fact]
        public void AncillaryBytesMayBeSkippedAndZeroBackPointerDiscardsPriorSlots()
        {
            MediaBufferAllocator allocator = new(1024);
            using Mp3BitReservoir reservoir = new(allocator, MpegAudioVersion.Mpeg1, 16);
            reservoir.Assemble([1, 2, 3, 4], 0, 8);
            Assert.Equal(new byte[] { 4, 5 }, reservoir.Assemble([5, 6, 7], 1, 16).ToArray());
            Assert.Equal(new byte[] { 8, 9 }, reservoir.Assemble([8, 9], 0, 16).ToArray());
            Assert.Equal(0, reservoir.RetainedBytes);
            Assert.Throws<InvalidDataException>(() => reservoir.Assemble([0], 1, 8).ToArray());
        }

        [Fact]
        public void InvalidExtentMissingHistoryAndCancellationRequireReset()
        {
            MediaBufferAllocator allocator = new(1024);
            using Mp3BitReservoir reservoir = new(allocator, MpegAudioVersion.Mpeg1, 2);
            Assert.Throws<InvalidDataException>(() => reservoir.Assemble([1], 1, 0).ToArray());
            reservoir.Reset();
            Assert.Throws<InvalidDataException>(() => reservoir.Assemble([1], 0, 9).ToArray());
            reservoir.Reset();
            Assert.Throws<NotSupportedException>(() => reservoir.Assemble([1, 2, 3], 0, 0).ToArray());
            reservoir.Reset();
            reservoir.Assemble([1, 2], 0, 0);
            Assert.Throws<OperationCanceledException>(() => reservoir.Assemble([], 2, 16, new CancellationToken(true)).ToArray());
            Assert.Equal(0, reservoir.RetainedBytes);
            Assert.Throws<InvalidOperationException>(() => reservoir.Assemble([3], 0, 8).ToArray());
            reservoir.Reset();
            Assert.Equal(new byte[] { 3 }, reservoir.Assemble([3], 0, 8).ToArray());
        }

        [Fact]
        public void WorkspaceUsesTheAllocatorAndDisposalIsIdempotent()
        {
            MediaBufferAllocator allocator = new(520);
            Assert.Throws<MediaMemoryLimitException>(() => new Mp3BitReservoir(allocator, MpegAudioVersion.Mpeg1, 10));
            Assert.Equal(0, allocator.AllocatedBytes);
            Mp3BitReservoir reservoir = new(allocator, MpegAudioVersion.Mpeg1, 9);
            Assert.Equal(520, allocator.AllocatedBytes);
            reservoir.Dispose();
            reservoir.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ObjectDisposedException>(() => reservoir.Reset());
            Assert.Throws<ObjectDisposedException>(() => reservoir.Assemble([], 0, 0).ToArray());
        }
    }
}
