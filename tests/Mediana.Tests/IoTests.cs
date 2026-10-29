// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    public class IoTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task StreamOwnershipIsExplicit(bool leaveOpen)
        {
            using MemoryStream stream = new([1, 2, 3, 4]);
            stream.Position = 2;
            StreamByteSource source = new(stream, leaveOpen);
            Assert.Equal(2, source.Length);
            byte[] result = new byte[2];
            Assert.Equal(2, await source.ReadAsync(result));
            Assert.Equal(new byte[] { 3, 4 }, result);
            Assert.Equal(0, await source.ReadAsync(result));
            await source.DisposeAsync();
            Assert.Equal(leaveOpen, stream.CanRead);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await source.ReadAsync(result));
        }
    }
}
