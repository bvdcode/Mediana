// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class MediaDecodeCleanupTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EveryDecoderIsReleasedEvenWhenDisposalThrows(bool secondThrows)
        {
            MediaBufferAllocator allocator = new(1024);
            EncodedStreamFormat syntax = new("test", "test", EncodedPacketBoundary.AccessUnit);
            EncodedStreamInfo a = new(0, MediaKind.Audio, "test", syntax, new(1, 48000));
            EncodedStreamInfo b = new(1, MediaKind.Audio, "test", syntax, new(1, 48000));
            CleanupTestDecoder first = new(true, allocator.Allocate(4));
            CleanupTestDecoder second = new(secondThrows, allocator.Allocate(4));
            using MediaDecodeGroup group = new(new CleanupTestReader([a, b]),
                [MediaDecodeTrack.Audio(a, first), MediaDecodeTrack.Audio(b, second)]);
            if (secondThrows)
            {
                AggregateException error = Assert.Throws<AggregateException>(group.Dispose);
                Assert.Equal(2, error.InnerExceptions.Count);
            }
            else { Assert.Throws<IOException>(group.Dispose); }
            Assert.True(first.Disposed);
            Assert.True(second.Disposed);
            Assert.Equal(0, allocator.AllocatedBytes);
            group.Dispose();
        }

        [Fact]
        public async Task OwnerCleanupPreservesOperationAndDependentFailures()
        {
            MediaBufferAllocator allocator = new(16);
            IOException operation = new("open failed");
            CleanupTestDecoder decoder = new(true, allocator.Allocate(4));
            CleanupTestReader owner = new([], throws: true);
            AggregateException error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await MediaResourceCleanup.DisposeAllAsync([decoder], owner, operation));
            Assert.Contains(operation, error.InnerExceptions);
            Assert.Equal(3, error.InnerExceptions.Count);
            Assert.True(owner.Disposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CleanupPreservesTheOperationFailureAlongsideReleaseFailures()
        {
            MediaBufferAllocator allocator = new(16);
            IOException operation = new("read failed");
            CleanupTestDecoder decoder = new(true, allocator.Allocate(4));
            AggregateException error = Assert.Throws<AggregateException>(() => MediaResourceCleanup.DisposeAll([decoder], operation));
            Assert.Same(operation, error.InnerExceptions[0]);
            Assert.Equal(2, error.InnerExceptions.Count);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
