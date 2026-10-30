// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class Yuv420AreaResizerTests
    {
        [Theory]
        [InlineData(ChromaLocation.Left)]
        [InlineData(ChromaLocation.Center)]
        [InlineData(ChromaLocation.TopLeft)]
        [InlineData(ChromaLocation.Top)]
        [InlineData(ChromaLocation.BottomLeft)]
        [InlineData(ChromaLocation.Bottom)]
        public void SitedChromaAndStridedPlanesMatchAnalyticBoxIntegrals(ChromaLocation location)
        {
            MediaBufferAllocator allocator = new(65536);
            using VideoFrame input = CreateFrame(10, 8, location, allocator, true);
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.Create(input.Format, 6, 4, allocator);
            using VideoFrame output = resizer.Apply(input);
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
            Assert.Equal(input.Format.Color, output.Format.Color);
            Assert.Equal(new SampleAspectRatio(10, 9), output.Format.SampleAspectRatio);
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < output.Planes[plane].Height; y++)
                {
                    for (int x = 0; x < output.Planes[plane].RowBytes; x++)
                    {
                        Assert.Equal(Reference(input, plane, x, y, output.Planes[plane].RowBytes, output.Planes[plane].Height), output.GetRow(plane, y)[x]);
                    }
                }
            }
            resizer.Dispose();
            input.Dispose();
            Assert.NotEmpty(output.Data.ToArray());
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void CenteredCheckerboardReducesToAnAverageInsteadOfAliasing()
        {
            MediaBufferAllocator allocator = new(65536);
            using VideoFrame input = CreateFrame(8, 8, ChromaLocation.Center, allocator, false, checkerboard: true);
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.Create(input.Format, 2, 2, allocator);
            using VideoFrame output = resizer.Apply(input);
            Assert.All(output.Data.ToArray(), value => Assert.Equal(128, value));
        }

        [Fact]
        public void IdentityIsExactAndFitPreservesDisplayAspectThroughEvenRounding()
        {
            MediaBufferAllocator allocator = new(65536);
            using VideoFrame input = CreateFrame(10, 8, ChromaLocation.Left, allocator, false);
            using Yuv420AreaResizer identity = Yuv420AreaResizer.Create(input.Format, 10, 8, allocator);
            using VideoFrame copy = identity.Apply(input);
            Assert.Equal(input.Data.ToArray(), copy.Data.ToArray());
            using Yuv420AreaResizer fit = Yuv420AreaResizer.CreateFit(input.Format, 7, 7, allocator);
            Assert.Equal(6, fit.OutputFormat.Width);
            Assert.Equal(4, fit.OutputFormat.Height);
            Assert.Equal(new SampleAspectRatio(10, 9), fit.OutputFormat.SampleAspectRatio);
            using Yuv420AreaResizer unchanged = Yuv420AreaResizer.CreateFit(input.Format, 100, 100, allocator);
            Assert.Equal(10, unchanged.OutputFormat.Width);
            Assert.Equal(8, unchanged.OutputFormat.Height);
        }

        [Fact]
        public void AllocationCancellationAndInvalidConfigurationsReleaseAllTemporaryStorage()
        {
            MediaBufferAllocator allocator = new(65536);
            using VideoFrame input = CreateFrame(10, 8, ChromaLocation.Left, allocator, false);
            Assert.Throws<NotSupportedException>(() => Yuv420AreaResizer.Create(input.Format, 11, 8, allocator));
            Assert.Throws<NotSupportedException>(() => Yuv420AreaResizer.Create(input.Format, 12, 8, allocator));
            Assert.ThrowsAny<OperationCanceledException>(() => Yuv420AreaResizer.Create(input.Format, 6, 4, allocator, new(true)));
            long held = allocator.AllocatedBytes;
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.Create(input.Format, 6, 4, allocator);
            long plan = allocator.AllocatedBytes;
            using WritableMediaBuffer occupied = allocator.Allocate((int)(allocator.LimitBytes - plan));
            Assert.Throws<MediaMemoryLimitException>(() => resizer.Apply(input));
            occupied.Dispose();
            Assert.Equal(plan, allocator.AllocatedBytes);
            Assert.ThrowsAny<OperationCanceledException>(() => resizer.Apply(input, new(true)));
            Assert.Equal(plan, allocator.AllocatedBytes);
            using VideoFrame output = resizer.Apply(input);
            resizer.Dispose();
            Assert.Throws<ObjectDisposedException>(() => resizer.Apply(input));
            output.Dispose();
            Assert.Equal(held, allocator.AllocatedBytes);
            input.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static VideoFrame CreateFrame(int width, int height, ChromaLocation location, MediaBufferAllocator allocator,
            bool padded, bool checkerboard = false)
        {
            VideoFormat format = new(width, height, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, location), new(4, 3));
            PlaneLayout[] planes = format.CreateTightLayout();
            if (padded)
            {
                int offset = 7;
                for (int plane = 0; plane < 3; plane++)
                {
                    planes[plane] = new(offset, planes[plane].Stride + 5, planes[plane].RowBytes, planes[plane].Height);
                    offset = checked((int)planes[plane].EndOffset + 3);
                }
            }
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            writable.Memory.Span.Fill(231);
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < planes[plane].Height; y++)
                {
                    for (int x = 0; x < planes[plane].RowBytes; x++)
                    {
                        writable.Memory.Span[planes[plane].Offset + y * planes[plane].Stride + x] =
                            checkerboard ? (byte)(((x + y) & 1) * 255) : (byte)((x * 23 + y * 31 + plane * 57) % 256);
                    }
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes, new(77, new(1, 1000)), new(40, new(1, 1000)));
        }

        private static byte Reference(VideoFrame input, int plane, int x, int y, int width, int height)
        {
            (decimal horizontal, decimal vertical) = input.Format.Color.ChromaLocation switch
            {
                ChromaLocation.Left => (0, 0.5m),
                ChromaLocation.Center => (0.5m, 0.5m),
                ChromaLocation.TopLeft => (0, 0),
                ChromaLocation.Top => (0.5m, 0),
                ChromaLocation.BottomLeft => (0, 1),
                ChromaLocation.Bottom => (0.5m, 1),
                _ => throw new ArgumentOutOfRangeException(nameof(input))
            };
            decimal scale = plane == 0 ? 1 : 2;
            decimal ratioX = input.Planes[plane].RowBytes / (decimal)width;
            decimal ratioY = input.Planes[plane].Height / (decimal)height;
            decimal shiftX = plane == 0 ? 0.5m : (horizontal + 0.5m) / scale;
            decimal shiftY = plane == 0 ? 0.5m : (vertical + 0.5m) / scale;
            decimal centerX = x * ratioX + shiftX * (ratioX - 1);
            decimal centerY = y * ratioY + shiftY * (ratioY - 1);
            decimal total = 0;
            for (int sy = 0; sy < input.Planes[plane].Height; sy++)
            {
                decimal top = sy == 0 ? decimal.MinValue : sy - 0.5m;
                decimal bottom = sy == input.Planes[plane].Height - 1 ? decimal.MaxValue : sy + 0.5m;
                decimal weightY = Math.Max(0, Math.Min(bottom, centerY + ratioY / 2) - Math.Max(top, centerY - ratioY / 2));
                for (int sx = 0; sx < input.Planes[plane].RowBytes; sx++)
                {
                    decimal left = sx == 0 ? decimal.MinValue : sx - 0.5m;
                    decimal right = sx == input.Planes[plane].RowBytes - 1 ? decimal.MaxValue : sx + 0.5m;
                    decimal weightX = Math.Max(0, Math.Min(right, centerX + ratioX / 2) - Math.Max(left, centerX - ratioX / 2));
                    total += input.GetRow(plane, sy)[sx] * weightX * weightY;
                }
            }
            decimal mean = decimal.Round(total / ratioX / ratioY, 12);
            return (byte)decimal.Round(mean, 0, MidpointRounding.AwayFromZero);
        }
    }
}
