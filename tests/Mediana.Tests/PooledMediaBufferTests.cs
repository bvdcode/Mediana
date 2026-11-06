// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class PooledMediaBufferTests
    {
        [Fact]
        public void RentalsAreClearedAndRetainedSlicesKeepTheFullCapacityUntilLastDisposal()
        {
            using TestMemoryPool pool = new(32);
            MediaBufferAllocator parent = new(64, pool), scope = parent.CreateScope(32);
            using WritableMediaBuffer writable = scope.Allocate(13);
            Assert.Equal(new byte[13], writable.Memory.ToArray());
            Assert.Equal(32, parent.AllocatedBytes);
            Assert.Equal(32, scope.AllocatedBytes);
            writable.Span.Fill(71);
            using MediaBuffer published = writable.Publish();
            using MediaBuffer slice = published.Slice(2, 3);
            published.Dispose(); writable.Dispose();
            Assert.Equal(0, pool.Returns);
            Assert.Equal(new byte[] { 71, 71, 71 }, slice.Memory.ToArray());
            slice.Dispose();
            Assert.Equal(1, pool.Returns);
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, scope.AllocatedBytes);
            using WritableMediaBuffer next = scope.Allocate(13);
            Assert.Equal(new byte[13], next.Memory.ToArray());
        }

        [Fact]
        public void FailedBudgetReservationReturnsTheRentalAndDoesNotChargeAncestors()
        {
            using TestMemoryPool pool = new(32);
            MediaBufferAllocator parent = new(16, pool), scope = parent.CreateScope(64);
            Assert.Throws<MediaMemoryLimitException>(() => scope.Allocate(4));
            Assert.Equal(1, pool.Returns);
            Assert.Equal(0, parent.AllocatedBytes);
            Assert.Equal(0, scope.AllocatedBytes);
        }

        [Fact]
        public void PooledPixelFramesRemainIndependentOfSubsequentCanvases()
        {
            using PixelBuffer first = new(37, 29);
            first.Clear(new(23, 57, 139, 211));
            using Mediana.Core.Video.VideoFrame frame = first.PublishVideoFrame();
            using PixelBuffer second = new(37, 29);
            Assert.All(second.Pixels.ToArray(), value => Assert.Equal(0, value));
            second.Clear(new(255, 0, 0));
            Assert.Equal(new byte[] { 23, 57, 139, 211 }, frame.GetRow(0, 0)[..4].ToArray());
        }
    }
}
