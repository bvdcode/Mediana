using Mediana.IO;

namespace Mediana.Tests
{
    public class RandomAccessCursorTests
    {
        [Fact]
        public async Task IndependentCursorsAndRangeReadsPreserveSourcePosition()
        {
            using MemoryStream stream = new([10, 20, 30]);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await source.ReadAsync(new byte[1]);
            await using RandomAccessCursorByteSource first = new(source, leaveOpen: true);
            await using RandomAccessCursorByteSource second = new(source, leaveOpen: true);
            byte[] data = new byte[1];
            Assert.Equal(3, first.Length);
            Assert.Equal(1, await first.ReadAsync(data));
            Assert.Equal(10, data[0]);
            Assert.Equal(1, await first.ReadAtAsync(2, data));
            Assert.Equal(30, data[0]);
            Assert.Equal(1, await first.ReadAsync(data));
            Assert.Equal(20, data[0]);
            Assert.Equal(1, await second.ReadAsync(data));
            Assert.Equal(10, data[0]);
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public async Task InvalidReadDoesNotAdvanceCursor()
        {
            await using InvalidRandomAccessSource source = new(2);
            await using RandomAccessCursorByteSource cursor = new(source, leaveOpen: true);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await cursor.ReadAsync(new byte[1]));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await cursor.ReadAtAsync(long.MaxValue, new byte[1]));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await cursor.ReadAsync(Memory<byte>.Empty, new CancellationToken(true)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DisposalHonorsOwnershipAndRejectsReads(bool leaveOpen)
        {
            using MemoryStream stream = new([1]);
            await using RandomAccessStreamByteSource source = new(stream);
            RandomAccessCursorByteSource cursor = new(source, leaveOpen);
            await cursor.DisposeAsync();
            await cursor.DisposeAsync();
            Assert.Equal(leaveOpen, stream.CanRead);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await cursor.ReadAtAsync(0, Memory<byte>.Empty));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await cursor.ReadAsync(new byte[1]));
        }
    }
}
