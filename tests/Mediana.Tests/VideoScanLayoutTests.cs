// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class VideoScanLayoutTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(6, 4)]
        [InlineData(1080, 540)]
        [InlineData(1082, 542)]
        public void InterlacedChromaSubsamplesEachFieldIndependently(int height, int chromaRows)
        {
            foreach (VideoScanMode mode in new[] { VideoScanMode.TopFieldFirst, VideoScanMode.BottomFieldFirst })
            {
                VideoFormat format = new(6, height, VideoPixelFormat.Yuv420P8, null, null, null, mode);
                PlaneLayout[] planes = format.CreateTightLayout();
                Assert.Equal(height, planes[0].Height);
                Assert.Equal(chromaRows, planes[1].Height);
                Assert.Equal(chromaRows, planes[2].Height);
                Assert.Equal(6 * height, planes[1].Offset);
                Assert.Equal(6 * height + 3 * chromaRows, planes[2].Offset);
                MediaBufferAllocator allocator = new(planes[^1].EndOffset);
                using (WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset)))
                using (MediaBuffer buffer = writable.Publish())
                using (VideoFrame frame = new(format, buffer, planes))
                using (VideoFrame retained = frame.Retain())
                using (VideoFrame timed = frame.WithTiming(new(0, new(1, 25)), new(1, new(1, 25))))
                {
                    Assert.Equal(mode, retained.Format.ScanMode);
                    Assert.Equal(mode, timed.Format.ScanMode);
                    Assert.Equal(planes[^1].EndOffset, allocator.AllocatedBytes);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(VideoScanMode.Progressive)]
        [InlineData(VideoScanMode.TopField)]
        [InlineData(VideoScanMode.BottomField)]
        public void SingleSurfacesUseTheirStoredSampleHeight(VideoScanMode mode)
        {
            VideoFormat format = new(6, 3, VideoPixelFormat.Yuv420P10, null, null, null, mode);
            if (mode == VideoScanMode.Progressive) { Assert.Equal(3, format.FullFrameHeight); }
            else { Assert.Null(format.FullFrameHeight); }
            PlaneLayout[] planes = format.CreateTightLayout();
            Assert.Equal(3, planes[0].Height);
            Assert.Equal(2, planes[1].Height);
            Assert.Equal(6, planes[1].RowBytes);
            Assert.Equal(60, planes[^1].EndOffset);
        }

        [Fact]
        public void AFieldCanDeclareEitherMatchingFullHeightWithoutGuessing()
        {
            foreach (int height in new[] { 5, 6 })
            {
                VideoFormat top = new(6, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopField, height);
                Assert.Equal(height, top.FullFrameHeight);
            }
            foreach (int height in new[] { 6, 7 })
            {
                VideoFormat bottom = new(6, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.BottomField, height);
                Assert.Equal(height, bottom.FullFrameHeight);
            }
            Assert.Throws<ArgumentException>(() => new VideoFormat(6, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopField, 7));
            Assert.Throws<ArgumentException>(() => new VideoFormat(6, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.BottomField, 5));
            Assert.Throws<ArgumentException>(() => new VideoFormat(6, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.Progressive, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoFormat(6, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopField, 0));
        }

        [Fact]
        public void DefaultLayoutAndInvalidScanKeepExplicitSemantics()
        {
            VideoFormat format = new(6, 6, VideoPixelFormat.Yuv420P8);
            Assert.Equal(VideoScanMode.Progressive, format.ScanMode);
            Assert.Equal(3, format.CreateTightLayout()[1].Height);
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoFormat(6, 6, VideoPixelFormat.Yuv420P8, null, null, null, (VideoScanMode)99));
        }
    }
}
