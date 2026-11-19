// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class MemoryTests
    {
        [Theory]
        [InlineData(8, 4096)]
        [InlineData(4096, 8)]
        public void ImpossiblePooledRequestsFailBeforeRenting(int parentLimit, int scopeLimit)
        {
            using CountingMemoryPool pool = new();
            MediaBufferAllocator parent = new(parentLimit, pool);
            MediaBufferAllocator scope = parent.CreateScope(scopeLimit);
            Assert.Throws<MediaMemoryLimitException>(() => scope.Allocate(1024));
            Assert.Equal(0, pool.Rentals);
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, scope.AllocatedBytes);
        }

        [Fact]
        public void FailedRentalRollsBackEveryBudget()
        {
            using TestMemoryPool pool = new(8);
            MediaBufferAllocator parent = new(1024, pool);
            MediaBufferAllocator scope = parent.CreateScope(1024);
            Assert.Throws<InvalidOperationException>(() => scope.Allocate(16));
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, scope.AllocatedBytes);
            using WritableMediaBuffer valid = scope.Allocate(8);
            Assert.Equal(8, parent.AllocatedBytes);
        }

        [Fact]
        public void WritableSpanSharesStorageAndPublicationPreservesRetainedLeases() =>
            WritableMediaSpanTestInput.VerifyPublication();

        [Fact]
        public void DisposedWritableStorageCannotExposeASpan() => WritableMediaSpanTestInput.VerifyDisposal();

        [Fact]
        public void RepeatedWritableSpanAccessAllocatesNoHeapStorage() => WritableMediaSpanTestInput.VerifyAllocation();

        [Fact]
        public void PublishingTransfersOwnershipAndRetainingSharesTheBudget()
        {
            MediaBufferAllocator allocator = new(8);
            using WritableMediaBuffer writable = allocator.Allocate(8);
            Assert.All(writable.Memory.ToArray(), value => Assert.Equal(0, value));
            writable.Memory.Span[0] = 42;
            MediaBuffer first = writable.Publish();
            using MediaBuffer second = first.Retain();
            Assert.Throws<ObjectDisposedException>(() => writable.Memory);
            first.Dispose();
            first.Dispose();
            Assert.Equal(8, allocator.AllocatedBytes);
            Assert.Equal(42, second.Memory.Span[0]);
            Assert.Throws<ObjectDisposedException>(() => first.Retain());
            Assert.Throws<MediaMemoryLimitException>(() => allocator.Allocate(1));
            second.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void NestedSlicesRetainOnlyTheirLogicalRangeAndKeepTheWholeAllocationBudgeted()
        {
            MediaBufferAllocator allocator = new(8);
            using WritableMediaBuffer writable = allocator.Allocate(8);
            new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }.CopyTo(writable.Memory);
            using MediaBuffer original = writable.Publish();
            using MediaBuffer outer = original.Slice(2, 4);
            using MediaBuffer nested = outer.Slice(1, 2);
            using MediaBuffer retained = nested.Retain();
            original.Dispose();
            outer.Dispose();
            nested.Dispose();
            Assert.Equal(new byte[] { 3, 4 }, retained.Memory.ToArray());
            Assert.Equal(8, allocator.AllocatedBytes);
            Assert.Throws<MediaMemoryLimitException>(() => allocator.Allocate(1));
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void SlicesValidateBoundsBeforeRetainingAndEmptyViewsHaveAnIndependentLease()
        {
            MediaBufferAllocator allocator = new(8);
            using WritableMediaBuffer writable = allocator.Allocate(8);
            using MediaBuffer original = writable.Publish();
            Assert.Throws<ArgumentOutOfRangeException>(() => original.Slice(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.Slice(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.Slice(9, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.Slice(1, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.Slice(int.MaxValue, int.MaxValue));
            using MediaBuffer empty = original.Slice(8, 0);
            Assert.Empty(empty.Memory.ToArray());
            original.Dispose();
            Assert.Throws<ObjectDisposedException>(() => original.Slice(0, 0));
            Assert.Equal(8, allocator.AllocatedBytes);
            empty.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task IndependentLeasesCanBeReleasedConcurrently()
        {
            MediaBufferAllocator allocator = new(64);
            using WritableMediaBuffer writable = allocator.Allocate(64);
            MediaBuffer original = writable.Publish();
            MediaBuffer[] leases = Enumerable.Range(0, 100).Select(_ => original.Retain()).ToArray();
            original.Dispose();
            await Task.WhenAll(leases.Select(lease => Task.Run(lease.Dispose)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ConcurrentAllocationsCannotExceedBudget()
        {
            MediaBufferAllocator allocator = new(64);
            using WritableMediaBuffer full = allocator.Allocate(64);
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
                Assert.Throws<MediaMemoryLimitException>(() => allocator.Allocate(1)))));
            Assert.Equal(64, allocator.AllocatedBytes);
            full.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
