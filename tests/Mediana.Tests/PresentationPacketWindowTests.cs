// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class PresentationPacketWindowTests
    {
        [Fact]
        public async Task WholePacketsTransferTheirOriginalLeaseWithoutReadingBeyondOneLookahead()
        {
            MediaBufferAllocator allocator = new(1024);
            TimedPacketSource source = new(allocator, 2, new(1, 1000), [-10, 0, 10, 20, 30, 40]);
            await using PresentationPacketWindow window = new(source, new(new(0, new(1, 100)), new(2, new(1, 100))));
            using MediaPacket first = (await window.ReadPacketAsync())!;
            using MediaPacket second = (await window.ReadPacketAsync())!;
            Assert.Equal(33, first.Data.Span[0]);
            Assert.Equal(34, second.Data.Span[0]);
            Assert.Null(await window.ReadPacketAsync());
            Assert.Equal(4, source.Reads);
            Assert.Null(await window.ReadPacketAsync());
            Assert.Equal(4, source.Reads);
            await window.DisposeAsync();
            Assert.True(source.IsDisposed);
            Assert.Equal(2, allocator.AllocatedBytes);
            first.Dispose(); second.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task EmptySelectionReadsNoPacketAndFailuresReleaseRejectedPackets()
        {
            MediaBufferAllocator allocator = new(1024);
            TimedPacketSource empty = new(allocator, 0, new(1, 1), [0]);
            await using (PresentationPacketWindow window = new(empty, new(new(0, new(1, 1)), new(0, new(1, 1)))))
            { Assert.Null(await window.ReadPacketAsync()); Assert.Equal(0, empty.Reads); }
            foreach (bool wrongStream in new[] { false, true })
            {
                TimedPacketSource invalid = new(allocator, 0, new(1, 1), [wrongStream ? 0 : null]) { WrongStream = wrongStream };
                await using PresentationPacketWindow window = new(invalid, new(new(0, new(1, 1)), new(10, new(1, 1))));
                if (wrongStream)
                {
                    await Assert.ThrowsAsync<InvalidDataException>(async () => await window.ReadPacketAsync());
                }
                else
                {
                    await Assert.ThrowsAsync<NotSupportedException>(async () => await window.ReadPacketAsync());
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await window.ReadPacketAsync());
            }
            TimedPacketSource reordered = new(allocator, 0, new(1, 1), [-1, -2, 0]);
            await using PresentationPacketWindow backwards = new(reordered, new(new(0, new(1, 1)), new(10, new(1, 1))));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await backwards.ReadPacketAsync());
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
