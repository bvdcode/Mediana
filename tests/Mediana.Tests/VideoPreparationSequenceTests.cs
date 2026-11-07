// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoPreparationSequenceTests
    {
        [Fact]
        public void RepeatedSourceSizeChangesKeepOneOutputFormatAndBoundedWorkingStorage()
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Center);
            VideoFormat large = new(32, 24, VideoPixelFormat.Yuv420P8, color, SampleAspectRatio.Square);
            VideoFormat small = new(16, 12, VideoPixelFormat.Yuv420P8, color, SampleAspectRatio.Square);
            using (VideoPreparationSequence preparation = new(large, VideoDisplayMatrix.Identity, allocator, new() { MaximumWidth = 8 }))
            {
                long workspace = allocator.AllocatedBytes;
                for (int i = 0; i < 40; i++)
                {
                    VideoFormat format = i % 2 == 0 ? large : small;
                    using (VideoFrame input = PlanarSrgbTests.Create(format, allocator, (_, _, _) => 128))
                    using (VideoFrame output = preparation.Apply(input))
                    {
                        Assert.Same(preparation.OutputFormat, output.Format); Assert.Equal(8, output.Format.Width); Assert.Equal(6, output.Format.Height);
                        Assert.All(output.Data.ToArray(), value => Assert.Equal(128, value));
                    }
                    Assert.Equal(workspace, allocator.AllocatedBytes);
                }
                using VideoFrame different = PlanarSrgbTests.Create(new(32, 16, VideoPixelFormat.Yuv420P8, color, SampleAspectRatio.Square), allocator, (_, _, _) => 0);
                long held = allocator.AllocatedBytes;
                Assert.Throws<NotSupportedException>(() => preparation.Apply(different)); Assert.Equal(held, allocator.AllocatedBytes);
                Assert.Throws<OperationCanceledException>(() => preparation.Apply(different, new(true))); Assert.Equal(held, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
