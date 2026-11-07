// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class ContainerAllocatorTests
    {
        [Fact]
        public async Task CallerPoolAndParentBudgetIncludeProbingAndRetainedPackets()
        {
            using CountingMemoryPool pool = new();
            MediaBufferAllocator allocator = new(128, pool);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11, 22]);
            ContainerSession session = await engine.OpenContainerAsync(source, allocator, new() { BufferBudgetBytes = 64 });
            using MediaPacket packet = Assert.IsType<MediaPacket>(await session.ReadPacketAsync());
            Assert.Equal(session.AllocatedBufferBytes, allocator.AllocatedBytes);
            Assert.True(allocator.AllocatedBytes >= packet.Data.Length);
            await session.DisposeAsync();
            Assert.Equal(11, packet.Data.Span[0]);
            Assert.True(allocator.AllocatedBytes > 0);
            Assert.False(source.IsDisposed);
            Assert.False(pool.Disposed);
            packet.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(pool.Rentals, pool.Returns);
            Assert.True(pool.Rentals >= 3);
        }

        [Theory]
        [InlineData(8, 64)]
        [InlineData(128, 8)]
        public async Task RoundedRentalCapacityIsEnforcedByBothBudgetsWithoutLeaks(int parentLimit, int inputLimit)
        {
            using CountingMemoryPool pool = new();
            MediaBufferAllocator allocator = new(parentLimit, pool);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "TEST"u8, 11]);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () =>
                await engine.OpenContainerAsync(source, allocator, new() { BufferBudgetBytes = inputLimit }));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(pool.Rentals, pool.Returns);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public async Task ContainerOpenFailureReturnsAllRentalsAndBorrowsThePool()
        {
            using CountingMemoryPool pool = new();
            MediaBufferAllocator allocator = new(128, pool);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new FixtureContainerFactory()).Build();
            await using ShortReadSource source = new([.. "FAIL"u8]);
            await Assert.ThrowsAsync<NotSupportedException>(async () =>
                await engine.OpenContainerAsync(source, allocator, new()));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(pool.Rentals, pool.Returns);
            Assert.False(pool.Disposed);
            Assert.False(source.IsDisposed);
        }
    }
}
