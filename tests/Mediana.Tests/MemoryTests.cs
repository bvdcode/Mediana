using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class MemoryTests
    {
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
