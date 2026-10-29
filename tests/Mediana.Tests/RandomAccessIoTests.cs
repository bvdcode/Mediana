// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.IO;

namespace Mediana.Tests
{
    public class RandomAccessIoTests
    {
        [Fact]
        public async Task OffsetsUseInitialPositionAndPreserveSequentialCursor()
        {
            using MemoryStream stream = new([99, 98, 10, 20, 30, 40]);
            stream.Position = 2;
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            Assert.Equal(4, source.Length);
            byte[] data = new byte[2];

            Assert.Equal(2, await source.ReadAtAsync(1, data));
            Assert.Equal(new byte[] { 20, 30 }, data);
            Assert.Equal(2, stream.Position);
            Assert.Equal(2, await source.ReadAsync(data));
            Assert.Equal(new byte[] { 10, 20 }, data);
            Assert.Equal(4, stream.Position);

            Assert.Equal(2, await source.ReadAtAsync(0, data));
            Assert.Equal(new byte[] { 10, 20 }, data);
            Assert.Equal(4, stream.Position);
            Assert.Equal(2, await source.ReadAsync(data));
            Assert.Equal(new byte[] { 30, 40 }, data);
        }

        [Fact]
        public async Task EndOfRangeReturnsShortReadThenEofWithoutMovingCursor()
        {
            using MemoryStream stream = new([10, 20, 30]);
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            byte[] data = new byte[4];
            Assert.Equal(1, await source.ReadAtAsync(2, data));
            Assert.Equal(30, data[0]);
            Assert.Equal(0, await source.ReadAtAsync(3, data));
            Assert.Equal(0, await source.ReadAtAsync(long.MaxValue, data));
            Assert.Equal(0, await source.ReadAtAsync(0, Memory<byte>.Empty));
            Assert.Equal(0, stream.Position);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DisposedSourceRejectsReadsEvenWhenStreamIsLeftOpen(bool leaveOpen)
        {
            using MemoryStream stream = new([1, 2]);
            RandomAccessStreamByteSource source = new(stream, leaveOpen);
            await source.DisposeAsync();
            await source.DisposeAsync();
            Assert.Equal(leaveOpen, stream.CanRead);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await source.ReadAtAsync(0, new byte[1]));
        }

        [Fact]
        public async Task FailedReadRestoresUnderlyingPosition()
        {
            using FailingReadStream stream = new([1, 2, 3]);
            stream.Position = 1;
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await Assert.ThrowsAsync<IOException>(async () => await source.ReadAtAsync(1, new byte[1]));
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public async Task CancellationAndInvalidOffsetsDoNotMoveTheCursor()
        {
            using MemoryStream stream = new([1, 2, 3]);
            stream.Position = 1;
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await source.ReadAtAsync(-1, new byte[1]));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await source.ReadAtAsync(0, new byte[1], new CancellationToken(true)));
            Assert.Equal(1, stream.Position);
        }

        [Fact]
        public void NonSeekableStreamIsRejectedAtConstruction()
        {
            Assert.Throws<ArgumentNullException>(() => new RandomAccessStreamByteSource(null!));
            using MemoryStream memory = new();
            using DeflateStream stream = new(memory, CompressionMode.Decompress);
            Assert.Throws<ArgumentException>(() => new RandomAccessStreamByteSource(stream));
        }

        [Fact]
        public async Task ExactReadHandlesShortReadsAndPreservesCursor()
        {
            using ShortReadStream stream = new([99, 10, 20, 30, 40]);
            stream.Position = 1;
            await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
            byte[] data = new byte[3];
            await source.ReadExactlyAtAsync(1, data);
            Assert.Equal(new byte[] { 20, 30, 40 }, data);
            Assert.Equal(1, stream.Position);
            await Assert.ThrowsAsync<EndOfStreamException>(async () => await source.ReadExactlyAtAsync(2, data));
            Assert.Equal(1, stream.Position);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        public async Task ExactReadRejectsInvalidSourceReadLengths(int count)
        {
            await using InvalidRandomAccessSource source = new(count);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadExactlyAtAsync(0, new byte[1]));
        }

        [Fact]
        public async Task ExactReadValidatesRangeAndCancellationForEmptyReads()
        {
            using MemoryStream stream = new([1, 2, 3]);
            await using RandomAccessStreamByteSource source = new(stream);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await source.ReadExactlyAtAsync(long.MaxValue, new byte[1]));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await source.ReadExactlyAtAsync(-1, Memory<byte>.Empty));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await source.ReadExactlyAtAsync(0, Memory<byte>.Empty, new CancellationToken(true)));
        }
    }
}
