// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class MediaBufferWorkspaceTests
    {
        [Fact]
        public void TemporaryBuffersShareTheCallerBudgetAndPoolWithoutOwningEither()
        {
            using CountingMemoryPool pool = new();
            MediaBufferAllocator allocator = new(128, pool);
            using MediaBufferWorkspace workspace = new(allocator);
            Memory<byte> first = workspace.Allocate(17);
            first.Span.Fill(31);
            Memory<byte> second = workspace.Allocate(33);
            second.Span.Fill(47);
            Assert.True(allocator.AllocatedBytes >= 50);
            Assert.Equal(31, first.Span[0]);
            Assert.Throws<MediaMemoryLimitException>(() => workspace.Allocate(1024));
            workspace.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(pool.Rentals, pool.Returns);
            Assert.False(pool.Disposed);
            Assert.Throws<ObjectDisposedException>(() => workspace.Allocate(1));
            using WritableMediaBuffer next = allocator.Allocate(1);
            Assert.Equal(1, next.Memory.Length);
            Assert.Throws<ArgumentNullException>(() => new MediaBufferWorkspace(null!));
        }
    }
}
