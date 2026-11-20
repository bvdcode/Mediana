// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoFieldViewTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(6)]
        [InlineData(8)]
        public void FieldViewsShareStorageAndRespectIndependentChromaGeometry(int height)
        {
            VideoFormat format = new(6, height, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.BottomFieldFirst);
            PlaneLayout[] planes = format.CreateTightLayout();
            MediaBufferAllocator allocator = new(1024);
            VideoFrame top;
            VideoFrame bottom;
            using (WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset))
            {
                for (int plane = 0; plane < planes.Length; plane++)
                {
                    for (int row = 0; row < planes[plane].Height; row++)
                    {
                        writable.Span.Slice(planes[plane].Offset + row * planes[plane].Stride, planes[plane].RowBytes).Fill((byte)(plane * 20 + row));
                    }
                }
                using MediaBuffer buffer = writable.Publish();
                using VideoFrame frame = new(format, buffer, planes);
                top = frame.GetField(VideoFieldParity.Top);
                bottom = frame.GetField(VideoFieldParity.Bottom);
                Assert.Equal(buffer.Memory, top.Data);
                Assert.Equal(buffer.Memory, bottom.Data);
                Assert.Equal(planes[^1].EndOffset, allocator.AllocatedBytes);
            }
            using (top)
            using (bottom)
            {
                Assert.Equal((height + 1) / 2, top.Format.Height);
                Assert.Equal(height / 2, bottom.Format.Height);
                Assert.Equal(VideoScanMode.TopField, top.Format.ScanMode);
                Assert.Equal(VideoScanMode.BottomField, bottom.Format.ScanMode);
                Assert.Equal(height, top.Format.FullFrameHeight);
                Assert.Equal(height, bottom.Format.FullFrameHeight);
                for (int plane = 0; plane < planes.Length; plane++)
                {
                    for (int row = 0; row < top.Planes[plane].Height; row++)
                    {
                        Assert.True(top.GetRow(plane, row).IndexOfAnyExcept((byte)(plane * 20 + row * 2)) < 0);
                    }
                    for (int row = 0; row < bottom.Planes[plane].Height; row++)
                    {
                        Assert.True(bottom.GetRow(plane, row).IndexOfAnyExcept((byte)(plane * 20 + row * 2 + 1)) < 0);
                    }
                }
                using VideoFrame retained = top.GetField(VideoFieldParity.Top);
                Assert.Equal(top.Data, retained.Data);
                Assert.Throws<ArgumentException>(() => top.GetField(VideoFieldParity.Bottom));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ChromaInterpolationMatchesTheSameRowsThroughAFieldView()
        {
            VideoColorInfo color = new(ChromaLocation: ChromaLocation.Left)
            {
                FieldChromaLocations = new(ChromaLocation.TopLeft, ChromaLocation.BottomLeft)
            };
            VideoFormat format = new(2, 8, VideoPixelFormat.Yuv420P8, color, null, null, VideoScanMode.TopFieldFirst);
            PlaneLayout[] planes = format.CreateTightLayout();
            MediaBufferAllocator allocator = new(1024);
            using WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset);
            new byte[] { 0, 240, 80, 160 }.CopyTo(writable.Span[planes[1].Offset..]);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(format, buffer, planes);
            foreach (VideoFieldParity parity in new[] { VideoFieldParity.Top, VideoFieldParity.Bottom })
            {
                using VideoFrame field = frame.GetField(parity);
                int offset = parity switch
                {
                    VideoFieldParity.Top => 0,
                    VideoFieldParity.Bottom => 1,
                    _ => throw new ArgumentOutOfRangeException(nameof(parity))
                };
                for (int row = 0; row < field.Format.Height; row++)
                {
                    BilinearChromaRow<byte> interleaved = new(frame, 1, row * 2 + offset, color.ChromaLocation, color.FieldChromaLocations);
                    BilinearChromaRow<byte> view = new(field, 1, row, color.ChromaLocation, color.FieldChromaLocations);
                    Assert.Equal(interleaved.Sample(0), view.Sample(0));
                }
            }
        }
    }
}
