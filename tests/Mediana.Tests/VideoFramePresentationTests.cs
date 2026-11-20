// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoFramePresentationTests
    {
        [Fact]
        public void PresentationHintsDescribeRepeatsWithoutChangingTimingOrCopyingPixels()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame original = Rgba(allocator))
            using (VideoFrame frame = original.WithPresentation(VideoFramePresentation.Fields(VideoFieldParity.Top, 3, VideoSourceScan.Progressive)))
            {
                long allocated = allocator.AllocatedBytes;
                using VideoFrame retained = frame.Retain();
                using VideoFrame timed = frame.WithTiming(new(90, new(1, 60)), new(3, new(1, 60)));
                using VideoFrame formatted = frame.WithFormat(frame.Format);
                using VideoFrame repeated = frame.WithPresentation(VideoFramePresentation.Frames(3));
                using VideoFrame cleared = frame.WithPresentation(null);
                foreach (VideoFrame copy in new[] { retained, timed, formatted, repeated, cleared })
                {
                    Assert.True(frame.Data.Equals(copy.Data));
                }
                Assert.Null(original.Presentation);
                Assert.Null(cleared.Presentation);
                Assert.Equal(frame.Presentation, retained.Presentation);
                Assert.Equal(frame.Presentation, timed.Presentation);
                Assert.Equal(frame.Presentation, formatted.Presentation);
                Assert.Equal(original.Timestamp, frame.Timestamp);
                Assert.Equal(original.Duration, frame.Duration);
                Assert.Equal(VideoPresentationUnit.Field, frame.Presentation!.Value.Unit);
                Assert.Equal(VideoFieldParity.Top, frame.Presentation.Value.FirstField);
                Assert.Equal(3, frame.Presentation.Value.DisplayCount);
                Assert.Equal(VideoSourceScan.Progressive, frame.Presentation.Value.SourceScan);
                Assert.Equal(VideoPresentationUnit.Frame, repeated.Presentation!.Value.Unit);
                Assert.Null(repeated.Presentation.Value.FirstField);
                Assert.Equal(3, repeated.Presentation.Value.DisplayCount);
                Assert.Equal(allocated, allocator.AllocatedBytes);
                frame.Dispose();
                Assert.Equal(255, retained.GetRow(0, 0)[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FieldSelectionKeepsSourceScanAndDoesNotInventFieldTiming()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = new(4, 4, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopFieldFirst);
            using (VideoFrame input = Create(format, allocator, VideoFramePresentation.Fields(VideoFieldParity.Top, 3, VideoSourceScan.Interlaced)))
            using (VideoFrame selected = input.GetField(VideoFieldParity.Bottom))
            {
                Assert.Equal(VideoFramePresentation.Fields(VideoFieldParity.Bottom, 1, VideoSourceScan.Interlaced), selected.Presentation);
                Assert.Equal(input.Timestamp, selected.Timestamp);
                Assert.Equal(input.Duration, selected.Duration);
                Assert.True(input.Data.Equals(selected.Data));
                Assert.Throws<ArgumentException>(() => selected.WithPresentation(VideoFramePresentation.Fields(VideoFieldParity.Top, 1)));
                Assert.Throws<ArgumentException>(() => selected.WithPresentation(VideoFramePresentation.Fields(VideoFieldParity.Bottom, 2)));
                Assert.Throws<ArgumentException>(() => selected.WithPresentation(VideoFramePresentation.Frames()));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ColorConversionAndResizingKeepCadenceAndContainerTiming()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFramePresentation presentation = VideoFramePresentation.Fields(VideoFieldParity.Bottom, 3, VideoSourceScan.Progressive);
            using (VideoFrame source = Rgba(allocator))
            using (VideoFrame input = source.WithPresentation(presentation))
            using (VideoFrame nearest = NearestNeighborResize.Apply(input, 4, 2, allocator))
            using (VideoFrame resized = RgbaResize.Apply(input, 4, 2, allocator))
            {
                RgbaToYuv420 conversion = new(input.Format, new(), allocator);
                using VideoFrame yuv = conversion.Apply(input);
                using Yuv420AreaResizer area = Yuv420AreaResizer.Create(yuv.Format, 4, 2, allocator);
                using VideoFrame scaled = area.Apply(yuv);
                using VideoFrame rgb = SrgbConverter.Apply(scaled, allocator);
                foreach (VideoFrame output in new[] { nearest, resized, yuv, scaled, rgb })
                {
                    Assert.Equal(presentation, output.Presentation);
                    Assert.Equal(input.Timestamp, output.Timestamp);
                    Assert.Equal(input.Duration, output.Duration);
                    Assert.Equal(VideoScanMode.Progressive, output.Format.ScanMode);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RejectsInvalidPresentationBeforeAcquiringABufferLease()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => VideoFramePresentation.Frames(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => VideoFramePresentation.Fields(VideoFieldParity.Top, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => VideoFramePresentation.Frames(1, (VideoSourceScan)99));
            Assert.Throws<ArgumentException>(() => VideoFramePresentation.Fields((VideoFieldParity)99, 1));
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame frame = Rgba(allocator))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => frame.WithPresentation(default(VideoFramePresentation)));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Rgba(MediaBufferAllocator allocator) => Create(new(8, 4, VideoPixelFormat.Rgba32,
            new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Rgb, ColorRange.Full)), allocator, null);

        private static VideoFrame Create(VideoFormat format, MediaBufferAllocator allocator, VideoFramePresentation? presentation)
        {
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[^1].EndOffset));
            writable.Span.Fill(255);
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, layout, new(12, new(1, 60)), new(3, new(1, 60)), presentation);
        }
    }
}
