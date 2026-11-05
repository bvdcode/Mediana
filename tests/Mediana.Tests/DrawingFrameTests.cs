// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using System.Numerics;
using Xunit;

namespace Mediana.Tests
{
    public class DrawingFrameTests
    {
        [Fact]
        public void DrawsBorrowedFrameWithoutAnIntermediatePixelAllocation()
        {
            MediaBufferAllocator allocator = new(2 * 2 * 4 * 2);
            using (PixelBuffer source = new(2, 2, allocator))
            using (PixelBuffer target = new(2, 2, allocator))
            {
                source.Clear(new(40, 60, 80));
                using VideoFrame frame = source.PublishVideoFrame();
                RasterCanvas canvas = new(target);
                canvas.DrawImage(frame, Matrix3x2.Identity);
                Assert.Equal(new Rgba32(40, 60, 80), target[1, 1]);
                Assert.Equal(32, allocator.AllocatedBytes);
                Assert.Equal(40, frame.GetRow(0, 0)[0]);
                Assert.Throws<OperationCanceledException>(() => canvas.DrawImage(frame, Matrix3x2.Identity, cancellationToken: new(true)));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
