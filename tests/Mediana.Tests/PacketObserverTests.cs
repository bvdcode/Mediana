// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class PacketObserverTests
    {
        [Fact]
        public async Task SuccessfulWritesAreObservedInOrderAndExplicitRetentionExtendsPacketLifetime()
        {
            MediaBufferAllocator allocator = new(2);
            await using TimedPacketSource source = new(allocator, 0, new(1, 1), [0, 1]);
            await using CollectingPacketWriter writer = new();
            MediaPacket? retained = null;
            int observed = 0;
            try
            {
                long count = await MediaPacketMerge.WriteAsync([source], writer, packet =>
                {
                    Assert.Equal(++observed, writer.Packets.Count);
                    Assert.Equal(writer.Packets[^1], packet.Data.ToArray());
                    Assert.False(writer.IsComplete);
                    retained ??= packet.Retain();
                }, default);
                Assert.Equal(2, count);
                Assert.Equal(2, observed);
                Assert.True(writer.IsComplete);
                Assert.Equal(1, allocator.AllocatedBytes);
                Assert.Equal(new byte[] { 0 }, retained!.Data.ToArray());
            }
            finally { retained?.Dispose(); }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ObserverFailureOrCancellationAbortsWithoutCompletingAndReleasesEveryPacket(bool cancel)
        {
            MediaBufferAllocator allocator = new(2);
            using CancellationTokenSource cancellation = new();
            await using TimedPacketSource source = new(allocator, 0, new(1, 1), [0, 1]);
            await using CollectingPacketWriter writer = new();
            Task<long> operation = MediaPacketMerge.WriteAsync([source], writer, _ =>
            {
                if (cancel) { cancellation.Cancel(); }
                else { throw new IOException("Observer failure."); }
            }, cancellation.Token);
            if (cancel) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation); }
            else { await Assert.ThrowsAsync<IOException>(() => operation); }
            Assert.Single(writer.Packets);
            Assert.Equal(1, source.Reads);
            Assert.False(writer.IsComplete || writer.IsDisposed || source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task AFailedWriteIsNeverReportedToTheObserver()
        {
            MediaBufferAllocator allocator = new(1);
            await using TimedPacketSource source = new(allocator, 0, new(1, 1), [0]);
            await using CollectingPacketWriter writer = new() { FailWrites = true };
            int observed = 0;
            await Assert.ThrowsAsync<IOException>(() => MediaPacketMerge.WriteAsync([source], writer, _ => observed++, default));
            Assert.Equal(0, observed);
            Assert.False(writer.IsComplete);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
