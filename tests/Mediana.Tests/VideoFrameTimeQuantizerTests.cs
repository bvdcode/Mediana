// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoFrameTimeQuantizerTests
    {
        [Theory]
        [InlineData(24, 1)]
        [InlineData(30, 1)]
        [InlineData(30000, 1001)]
        public void RoundedBoundariesPreserveLongTermDurationWithoutCopyingPixels(int rate, int period)
        {
            MediaBufferAllocator allocator = new(4);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            using MediaBuffer pixels = writable.Publish();
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            TimeBase source = new(period, rate);
            TimeBase target = new(1, 1000);
            VideoFrameTimeQuantizer clock = new(target);
            long sum = 0;
            for (int index = 0; index < 10000; index++)
            {
                using VideoFrame frame = new(format, pixels, format.CreateTightLayout(), new(index, source), new(1, source));
                using VideoFrame quantized = clock.Apply(frame);
                Assert.Equal(new MediaTimestamp(index, source).Rescale(target, TimestampRounding.NearestEven), quantized.Timestamp);
                sum += quantized.Duration!.Value.Value;
                Assert.Equal(new MediaTimestamp(index + 1, source).Rescale(target, TimestampRounding.NearestEven).Value, sum);
                Assert.True(frame.Data.Equals(quantized.Data));
                Assert.Equal(4, allocator.AllocatedBytes);
            }
        }
    }
}
