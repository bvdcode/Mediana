// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoPixelFormatTests
    {
        [Fact]
        public void PlanarEqualityUsesSampleMeaningAndLayoutInsteadOfNamesOrInstances()
        {
            VideoPixelFormat equivalent = new PlanarVideoPixelFormat("custom-name", PlanarColorModel.Yuv, 8, 2, 2);
            Assert.True(equivalent == VideoPixelFormat.Yuv420P8);
            Assert.Equal(equivalent.GetHashCode(), VideoPixelFormat.Yuv420P8.GetHashCode());
            Assert.NotEqual(equivalent, new PlanarVideoPixelFormat("custom-name", PlanarColorModel.Yuv, 10, 2, 2));
            Assert.NotEqual(equivalent, new PlanarVideoPixelFormat("custom-name", PlanarColorModel.Yuv, 8));
            Assert.NotEqual(new PlanarVideoPixelFormat("same", PlanarColorModel.Yuv, 8),
                new PlanarVideoPixelFormat("same", PlanarColorModel.Gbr, 8));
            Assert.NotEqual(VideoPixelFormat.Rgba32, new VideoPixelFormat("bgra32", new VideoPlaneFormat(1, 1, 4)));
            Assert.Equal(VideoPixelFormat.Rgba32, new VideoPixelFormat("rgba32", new VideoPlaneFormat(1, 1, 4)));
        }

        [Fact]
        public void PreparationAcceptsEquivalentDescriptorsWithoutCopyingPixels()
        {
            MediaBufferAllocator allocator = new(1024);
            VideoFormat expected = new(2, 2, VideoPixelFormat.Yuv420P8);
            VideoFormat actual = new(2, 2, new PlanarVideoPixelFormat("external", PlanarColorModel.Yuv, 8, 2, 2));
            using WritableMediaBuffer writable = allocator.Allocate(6);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(actual, buffer, actual.CreateTightLayout());
            using VideoPreparationSequence sequence = new(expected, VideoDisplayMatrix.Identity, allocator);
            using VideoFrame prepared = sequence.Apply(frame);
            Assert.Equal(frame.Data, prepared.Data);
            Assert.Equal(6, allocator.AllocatedBytes);
        }
    }
}
