// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    public class ReadOnlyMemoryStreamTests
    {
        [Fact]
        public async Task StreamBorrowsTheExactSliceAndSupportsSyncAndAsyncReads()
        {
            byte[] bytes = [9, 1, 2, 3, 9];
            using ReadOnlyMemoryStream stream = new(bytes.AsMemory(1, 3));
            bytes[1] = 7;
            Assert.True(stream.CanRead);
            Assert.True(stream.CanSeek);
            Assert.False(stream.CanWrite);
            Assert.Equal(3, stream.Length);
            Assert.Equal(7, stream.ReadByte());
            byte[] result = new byte[4];
            Assert.Equal(1, await stream.ReadAsync(result, 1, 1, CancellationToken.None));
            Assert.Equal(1, await stream.ReadAsync(result.AsMemory(2)));
            Assert.Equal(new byte[] { 0, 2, 3, 0 }, result);
            Assert.Equal(0, stream.Read(Span<byte>.Empty));
            Assert.Equal(0, await stream.ReadAsync(result));
            Assert.Equal(-1, stream.ReadByte());
            Assert.Equal(3, stream.Position);
            Assert.Equal(1, stream.Seek(-2, SeekOrigin.End));
            Assert.Equal(2, stream.ReadByte());
            Assert.Equal(1, stream.Seek(-1, SeekOrigin.Current));
            Assert.Equal(0, stream.Seek(0, SeekOrigin.Begin));
            Assert.Equal(7, stream.ReadByte());
        }

        [Fact]
        public void PositionBoundsDoNotCorruptTheCursor()
        {
            using ReadOnlyMemoryStream stream = new(new byte[3]);
            Assert.Equal(1, stream.Read(new byte[1], 0, 1));
            stream.Position = long.MaxValue;
            Assert.Equal(-1, stream.ReadByte());
            Assert.Throws<OverflowException>(() => stream.Seek(1, SeekOrigin.Current));
            Assert.Equal(long.MaxValue, stream.Position);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
            Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)42));
            Assert.Equal(long.MaxValue, stream.Position);
            Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
            Assert.Throws<NotSupportedException>(() => stream.WriteByte(1));
        }

        [Fact]
        public async Task CancellationAndDisposalReleaseTheViewWithoutChangingMemory()
        {
            byte[] data = [1, 2, 3];
            ReadOnlyMemoryStream stream = new(data);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => Assert.Equal(1, await stream.ReadAsync(new byte[1], new CancellationToken(true))));
            Assert.Equal(0, stream.Position);
            await stream.FlushAsync();
            await stream.DisposeAsync();
            Assert.False(stream.CanRead);
            Assert.False(stream.CanSeek);
            Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
            Assert.Throws<ObjectDisposedException>(() => stream.Length);
            Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
            Assert.Throws<ObjectDisposedException>(() => stream.Flush());
            Assert.Equal(new byte[] { 1, 2, 3 }, data);
        }
    }
}
