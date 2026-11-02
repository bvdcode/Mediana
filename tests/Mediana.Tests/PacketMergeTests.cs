// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class PacketMergeTests
    {
        [Fact]
        public Task DifferentClocksPrimingAndTiesUseOnlyOnePendingPacketPerStream() => PacketMergeTestInput.VerifyAsync();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SourceOrWriterFailureReleasesEveryPendingPacketWithoutCompletingBorrowedComponents(bool sourceFailure)
        {
            MediaBufferAllocator allocator = new(2);
            await using TimedPacketSource first = new(allocator, 0, new(1, 1), [0, 1]);
            await using TimedPacketSource second = new(allocator, 1, new(1, 1), [0, 1]) { FailReads = sourceFailure };
            await using CollectingPacketWriter writer = new() { FailWrites = !sourceFailure };
            await Assert.ThrowsAsync<IOException>(() => MediaPacketMerge.WriteAsync([first, second], writer));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(writer.IsComplete || writer.IsDisposed || first.IsDisposed || second.IsDisposed);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task UnknownWrongStreamAndNonIncreasingDecodeTimesFailWithoutLeaking(bool wrongStream, bool duplicate)
        {
            MediaBufferAllocator allocator = new(2);
            await using TimedPacketSource source = new(allocator, 0, new(1, 1), duplicate ? [0, 0] : wrongStream ? [0] : [null]) { WrongStream = wrongStream };
            await using CollectingPacketWriter writer = new();
            if (wrongStream || duplicate)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => MediaPacketMerge.WriteAsync([source], writer));
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => MediaPacketMerge.WriteAsync([source], writer));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(writer.IsComplete);
        }

        [Fact]
        public async Task CancellationAfterSourceTransferReleasesAllPackets()
        {
            MediaBufferAllocator allocator = new(2);
            using CancellationTokenSource cancellation = new();
            await using TimedPacketSource source = new(allocator, 0, new(1, 1), [0]) { AfterRead = cancellation.Cancel };
            await using CollectingPacketWriter writer = new();
            await Assert.ThrowsAsync<OperationCanceledException>(() => MediaPacketMerge.WriteAsync([source], writer, cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(writer.IsComplete);
        }
    }
}
