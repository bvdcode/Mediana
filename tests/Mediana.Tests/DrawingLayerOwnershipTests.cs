// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Drawing;

namespace Mediana.Tests
{
    public class DrawingLayerOwnershipTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void LayersShareTheOutputBudgetAndReleaseTemporaryStorage(bool knockout, bool fail)
        {
            MediaBufferAllocator allocator = new(12);
            using PixelBuffer output = new(1, 1, allocator);
            RasterCanvas canvas = new(output);
            void Draw()
            {
                canvas.DrawLayer(layer =>
                {
                    Assert.Equal(8, allocator.AllocatedBytes);
                    layer.DrawObject(element =>
                    {
                        Assert.Equal(knockout ? 12 : 8, allocator.AllocatedBytes);
                        element.Buffer[0, 0] = new Rgba32(255, 0, 0, 255);
                        if (fail)
                        {
                            throw new InvalidOperationException("Drawing failed.");
                        }
                    });
                }, knockout);
            }
            if (fail)
            {
                Assert.Throws<InvalidOperationException>(Draw);
            }
            else
            {
                Draw();
            }
            Assert.Equal(4, allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void NestedAllocationFailureReleasesEveryTemporaryLayer()
        {
            MediaBufferAllocator allocator = new(8);
            using PixelBuffer output = new(1, 1, allocator);
            RasterCanvas canvas = new(output);
            Assert.Throws<MediaMemoryLimitException>(() => canvas.DrawLayer(layer => layer.DrawLayer(_ => { })));
            Assert.Equal(4, allocator.AllocatedBytes);
        }

        [Fact]
        public void CancellationReleasesTheLayerAndKeepsTheOutputOwner()
        {
            MediaBufferAllocator allocator = new(8);
            using PixelBuffer output = new(1, 1, allocator);
            using CancellationTokenSource cancellation = new();
            RasterCanvas canvas = new(output);
            Assert.Throws<OperationCanceledException>(() => canvas.DrawLayer(_ => cancellation.Cancel(), cancellationToken: cancellation.Token));
            Assert.Equal(4, allocator.AllocatedBytes);
        }
    }
}
