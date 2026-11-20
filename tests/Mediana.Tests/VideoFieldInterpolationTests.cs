// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoFieldInterpolationTests
    {
        [Theory]
        [InlineData(8, 1)]
        [InlineData(8, 2)]
        [InlineData(10, 1)]
        [InlineData(10, 2)]
        [InlineData(16, 1)]
        [InlineData(16, 2)]
        public void ReconstructsIndependentFieldsWithOddSizesAndPaddedRows(int depth, int vertical)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            foreach (int width in new[] { 1, 7, 64, 257 })
            {
                foreach (int height in new[] { 2, 3, 5, 6, 7, 8, 9 })
                {
                    PlanarVideoPixelFormat pixels = new("test-yuv", PlanarColorModel.Yuv, depth, 2, vertical);
                    VideoFormat format = new(width, height, pixels, null, null, null, VideoScanMode.BottomFieldFirst);
                    using VideoFrame input = Create(format, allocator, (plane, row, column) => 20 + plane * 60 + row * 3 + column % 7);
                    foreach (VideoFieldParity parity in new[] { VideoFieldParity.Top, VideoFieldParity.Bottom })
                    {
                        using VideoFrame field = input.GetField(parity);
                        using VideoFrame output = VideoFieldInterpolator.Apply(field, height, allocator);
                        Assert.Equal(VideoScanMode.Progressive, output.Format.ScanMode);
                        Assert.Equal(height, output.Format.Height);
                        int offset = parity switch
                        {
                            VideoFieldParity.Top => 0,
                            VideoFieldParity.Bottom => 1,
                            _ => throw new ArgumentOutOfRangeException(nameof(parity))
                        };
                        for (int plane = 0; plane < output.Planes.Count; plane++)
                        {
                            int lastSourceRow = (field.Planes[plane].Height - 1) * 2 + offset;
                            for (int row = 0; row < output.Planes[plane].Height; row++)
                            {
                                int sourceRow = Math.Clamp(row, offset, lastSourceRow);
                                for (int column = 0; column < output.Planes[plane].RowBytes / pixels.Planes[plane].BytesPerElement; column++)
                                {
                                    int expected = 20 + plane * 60 + sourceRow * 3 + column % 7;
                                    Assert.Equal(expected, Read(output.GetRow(plane, row), column, depth));
                                }
                            }
                        }
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        public void IntegerAveragesRoundUpWithoutOverflow(int depth)
        {
            int width = 65539;
            int maximum = (1 << depth) - 1;
            PlanarVideoPixelFormat pixels = new("test-gbr", PlanarColorModel.Gbr, depth);
            VideoFormat format = new(width, 2, pixels, null, null, null, VideoScanMode.TopField);
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using (VideoFrame input = Create(format, allocator, (_, row, column) => row switch
            {
                0 => depth == 8 ? column & 255 : column & maximum,
                1 => depth == 8 ? (column >> 8) & 255 : maximum - (column & maximum),
                _ => throw new ArgumentOutOfRangeException(nameof(row))
            }))
            using (VideoFrame output = VideoFieldInterpolator.Apply(input, 3, allocator))
            {
                for (int plane = 0; plane < output.Planes.Count; plane++)
                {
                    Assert.True(input.GetRow(plane, 0).SequenceEqual(output.GetRow(plane, 0)));
                    Assert.True(input.GetRow(plane, 1).SequenceEqual(output.GetRow(plane, 2)));
                    for (int column = 0; column < width; column++)
                    {
                        int expected = (Read(input.GetRow(plane, 0), column, depth) + Read(input.GetRow(plane, 1), column, depth) + 1) / 2;
                        Assert.Equal(expected, Read(output.GetRow(plane, 1), column, depth));
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void PreservesTimingAndSelectedChromaLocationWithoutDuplicatingInputStorage()
        {
            VideoColorInfo color = new(ChromaLocation: ChromaLocation.Unspecified)
            {
                FieldChromaLocations = new(ChromaLocation.TopLeft, ChromaLocation.Bottom)
            };
            CicpColorDescription description = new(1, 1, 1, false);
            VideoFormat format = new(7, 5, VideoPixelFormat.Yuv420P10, color, new(4, 3), description, VideoScanMode.TopFieldFirst);
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame input = Create(format, allocator, (_, _, _) => 17);
            using VideoFrame field = input.GetField(VideoFieldParity.Bottom);
            using VideoFrame timed = field.WithTiming(new(15, new(1, 50)), new(1, new(1, 50)));
            using VideoFrame tagged = timed.WithPresentation(VideoFramePresentation.Fields(VideoFieldParity.Bottom, 1, VideoSourceScan.Interlaced));
            long before = allocator.AllocatedBytes;
            using (VideoFrame output = VideoFieldInterpolator.Apply(tagged, 5, allocator))
            {
                Assert.Equal(before + output.Planes[^1].EndOffset, allocator.AllocatedBytes);
                Assert.Equal(timed.Timestamp, output.Timestamp);
                Assert.Equal(timed.Duration, output.Duration);
                Assert.Equal(format.SampleAspectRatio, output.Format.SampleAspectRatio);
                Assert.Same(description, output.Format.ColorDescription);
                Assert.Equal(ChromaLocation.Bottom, output.Format.Color.ChromaLocation);
                Assert.Null(output.Format.Color.FieldChromaLocations);
                Assert.Equal(VideoFramePresentation.Frames(1, VideoSourceScan.Interlaced), output.Presentation);
            }
            Assert.Equal(before, allocator.AllocatedBytes);
        }

        [Fact]
        public void ChromaReconstructionKeepsFullFrameSamplePositionsForBothFields()
        {
            MediaBufferAllocator allocator = new(4096);
            foreach (ChromaLocation location in new[]
            {
                ChromaLocation.Left, ChromaLocation.Center, ChromaLocation.TopLeft,
                ChromaLocation.Top, ChromaLocation.BottomLeft, ChromaLocation.Bottom
            })
            {
                VideoColorInfo color = new(ChromaLocation: location);
                VideoFormat format = new(4, 16, VideoPixelFormat.Yuv420P10, color, null, null, VideoScanMode.TopFieldFirst);
                using VideoFrame source = Create(format, allocator, (_, row, _) => row / 2 * 80);
                foreach (VideoFieldParity parity in new[] { VideoFieldParity.Top, VideoFieldParity.Bottom })
                {
                    using VideoFrame field = source.GetField(parity);
                    using VideoFrame result = VideoFieldInterpolator.Apply(field, 16, allocator);
                    int parityOffset = parity switch
                    {
                        VideoFieldParity.Top => 0,
                        VideoFieldParity.Bottom => 1,
                        _ => throw new ArgumentOutOfRangeException(nameof(parity))
                    };
                    double firstPosition = ChromaSamplePosition.InQuarterFieldLumaSamples(location, parity).Vertical / 2d + parityOffset;
                    double outputOffset = ChromaSamplePosition.InHalfLumaSamples(location).Vertical / 2d;
                    for (int row = 0; row < result.Planes[1].Height; row++)
                    {
                        double position = row * 2 + outputOffset;
                        int expected = (int)Math.Clamp((position - firstPosition) / 4 * 80, 0, 240);
                        Assert.Equal(expected, Read(result.GetRow(1, row), 0, 10));
                    }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void StraightRgbaInterpolationWeightsColorByAlpha()
        {
            MediaBufferAllocator allocator = new(1024);
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, null, null, VideoScanMode.TopField);
            byte[] rows = [255, 0, 0, 255, 100, 20, 30, 0, 0, 255, 0, 0, 200, 40, 60, 0];
            using VideoFrame input = CreateBytes(format, allocator, rows);
            using VideoFrame output = VideoFieldInterpolator.Apply(input, 3, allocator);
            Assert.Equal(new byte[] { 255, 0, 0, 128, 0, 0, 0, 0 }, output.GetRow(0, 1).ToArray());
            Assert.True(input.GetRow(0, 0).SequenceEqual(output.GetRow(0, 0)));
            Assert.True(input.GetRow(0, 1).SequenceEqual(output.GetRow(0, 2)));
        }

        [Fact]
        public void PremultipliedFloatInterpolationPreservesRangeWithoutOverflow()
        {
            MediaBufferAllocator allocator = new(1024);
            VideoFormat format = new(1, 2, VideoPixelFormat.RgbaFloat32Premultiplied, null, null, null, VideoScanMode.BottomField);
            float[] rows = [float.MaxValue, -2, 4, 0.25f, float.MaxValue, 4, 8, 0.75f];
            using VideoFrame input = CreateBytes(format, allocator, MemoryMarshal.AsBytes(rows.AsSpan()).ToArray());
            using VideoFrame output = VideoFieldInterpolator.Apply(input, 4, allocator);
            Assert.Equal(new float[] { float.MaxValue, 1, 6, 0.5f }, MemoryMarshal.Cast<byte, float>(output.GetRow(0, 2)).ToArray());
        }

        [Fact]
        public void RejectsWrongGeometryAndHonorsCancellationAndMemoryBudget()
        {
            MediaBufferAllocator allocator = new(1024);
            VideoFormat format = new(2, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopFieldFirst);
            using VideoFrame input = Create(format, allocator, (_, _, _) => 1);
            using VideoFrame top = input.GetField(VideoFieldParity.Top);
            using VideoFrame bottom = input.GetField(VideoFieldParity.Bottom);
            long before = allocator.AllocatedBytes;
            Assert.Throws<ArgumentException>(() => VideoFieldInterpolator.Apply(input, 3, allocator));
            Assert.Throws<ArgumentException>(() => VideoFieldInterpolator.Apply(top, 2, allocator));
            Assert.Throws<ArgumentException>(() => VideoFieldInterpolator.Apply(top, 4, allocator));
            Assert.Throws<ArgumentException>(() => VideoFieldInterpolator.Apply(bottom, 4, allocator));
            Assert.Throws<OperationCanceledException>(() => VideoFieldInterpolator.Apply(top, 3, allocator, new(true)));
            MediaBufferAllocator small = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => VideoFieldInterpolator.Apply(top, 3, small));
            Assert.Equal(0, small.AllocatedBytes);
            Assert.Equal(before, allocator.AllocatedBytes);
        }

        private static int Read(ReadOnlySpan<byte> row, int column, int depth) => depth <= 8
            ? row[column] : MemoryMarshal.Cast<byte, ushort>(row)[column];

        private static VideoFrame Create(VideoFormat format, MediaBufferAllocator allocator, Func<int, int, int, int> sample)
        {
            PlaneLayout[] layout = format.CreateTightLayout();
            int offset = 11;
            for (int plane = 0; plane < layout.Length; plane++)
            {
                layout[plane] = new(offset, layout[plane].Stride + 14, layout[plane].RowBytes, layout[plane].Height);
                offset = checked((int)layout[plane].EndOffset + 19);
            }
            using WritableMediaBuffer writable = allocator.Allocate(offset);
            writable.Span.Fill(239);
            for (int plane = 0; plane < layout.Length; plane++)
            {
                int bytes = format.PixelFormat.Planes[plane].BytesPerElement;
                for (int row = 0; row < layout[plane].Height; row++)
                {
                    Span<byte> destination = writable.Span.Slice(layout[plane].Offset + row * layout[plane].Stride, layout[plane].RowBytes);
                    for (int column = 0; column < destination.Length / bytes; column++)
                    {
                        if (bytes == 1) { destination[column] = (byte)sample(plane, row, column); }
                        else { MemoryMarshal.Cast<byte, ushort>(destination)[column] = (ushort)sample(plane, row, column); }
                    }
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, layout);
        }

        private static VideoFrame CreateBytes(VideoFormat format, MediaBufferAllocator allocator, byte[] values)
        {
            using WritableMediaBuffer writable = allocator.Allocate(values.Length);
            values.CopyTo(writable.Span);
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, format.CreateTightLayout());
        }
    }
}
