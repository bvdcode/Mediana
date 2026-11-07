// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class RgbVideoConversionTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(7, 5)]
        [InlineData(32, 18)]
        public void OpaquePaddedRgbMatchesIndependentDecimalMatricesAndChromaAverages(int width, int height)
        {
            foreach ((MatrixCoefficients matrix, decimal red, decimal blue) in new[]
            {
                (MatrixCoefficients.Bt601, 0.299m, 0.114m), (MatrixCoefficients.Bt709, 0.2126m, 0.0722m),
                (MatrixCoefficients.Fcc, 0.30m, 0.11m), (MatrixCoefficients.Smpte240M, 0.212m, 0.087m),
                (MatrixCoefficients.Bt2020NonConstant, 0.2627m, 0.0593m)
            })
            foreach (ColorRange range in new[] { ColorRange.Full, ColorRange.Limited })
            {
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame input = Input(width, height, allocator))
                {
                    byte[] unchanged = input.Data.ToArray();
                    long baseline = allocator.AllocatedBytes;
                    RgbaToYuv420 conversion = new(input.Format, new() { Matrix = matrix, Range = range }, allocator);
                    using (VideoFrame output = conversion.Apply(input))
                    {
                        Assert.Equal(input.Timestamp, output.Timestamp);
                        Assert.Equal(input.Duration, output.Duration);
                        Assert.Equal(input.Format.SampleAspectRatio, output.Format.SampleAspectRatio);
                        Assert.Equal(input.Format.Color.Primaries, output.Format.Color.Primaries);
                        Assert.Equal(input.Format.Color.Transfer, output.Format.Color.Transfer);
                        Assert.Equal(ChromaLocation.Center, output.Format.Color.ChromaLocation);
                        decimal yScale = range == ColorRange.Full ? 255 : 219;
                        decimal yOffset = range == ColorRange.Full ? 0 : 16;
                        decimal chromaScale = range == ColorRange.Full ? 255 : 224;
                        for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            ReadOnlySpan<byte> pixel = input.GetRow(0, y).Slice(x * 4, 4);
                            decimal luma = red * pixel[0] + (1 - red - blue) * pixel[1] + blue * pixel[2];
                            Assert.InRange(Math.Abs(output.GetRow(0, y)[x] - Round(yOffset + luma * yScale / 255)), 0, 1);
                        }
                        for (int top = 0; top < height; top += 2)
                        for (int left = 0; left < width; left += 2)
                        {
                            decimal r = 0, g = 0, b = 0, count = 0;
                            for (int y = top; y < Math.Min(top + 2, height); y++)
                            for (int x = left; x < Math.Min(left + 2, width); x++)
                            {
                                ReadOnlySpan<byte> pixel = input.GetRow(0, y).Slice(x * 4, 4);
                                r += pixel[0]; g += pixel[1]; b += pixel[2]; count++;
                            }
                            decimal luma = red * r + (1 - red - blue) * g + blue * b;
                            int cb = Round(128 + chromaScale * (b - luma) / (510 * (1 - blue) * count));
                            int cr = Round(128 + chromaScale * (r - luma) / (510 * (1 - red) * count));
                            Assert.InRange(Math.Abs(output.GetRow(1, top / 2)[left / 2] - cb), 0, 1);
                            Assert.InRange(Math.Abs(output.GetRow(2, top / 2)[left / 2] - cr), 0, 1);
                        }
                        Assert.Equal(unchanged, input.Data.ToArray());
                    }
                    Assert.Equal(baseline, allocator.AllocatedBytes);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public void SharedPreparationRequiresExplicitConversionAndKeepsOrientationAndAspect()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame input = Input(8, 6, allocator);
            Assert.Throws<NotSupportedException>(() => new VideoPreparation(input.Format, VideoDisplayMatrix.Identity, allocator));
            using VideoPreparation preparation = new(input.Format, new(0, 1, 0, -1, 0, 0, 0, 0, 1), allocator,
                new() { RgbConversion = new(), MaximumWidth = 4, MaximumHeight = 8 });
            using VideoFrame output = preparation.Apply(input);
            Assert.Equal(VideoPixelFormat.Yuv420P8, output.Format.PixelFormat);
            Assert.Equal(4, output.Format.Width);
            Assert.Equal(4, output.Format.Height);
            Assert.Equal(new SampleAspectRatio(3, 5), output.Format.SampleAspectRatio);
        }

        [Fact]
        public void CancellationTransparencyAndBudgetFailuresReleaseTheOutputAllocation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame input = Input(8, 6, allocator, transparent: true);
            long baseline = allocator.AllocatedBytes;
            RgbaToYuv420 conversion = new(input.Format, new(), allocator);
            Assert.Throws<NotSupportedException>(() => conversion.Apply(input));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            Assert.ThrowsAny<OperationCanceledException>(() => conversion.Apply(input, new CancellationToken(true)));
            Assert.Equal(baseline, allocator.AllocatedBytes);
            MediaBufferAllocator tiny = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => new RgbaToYuv420(input.Format, new(), tiny).Apply(input));
            Assert.Equal(0, tiny.AllocatedBytes);
        }

        internal static VideoFrame Input(int width, int height, MediaBufferAllocator allocator, bool transparent = false)
        {
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, new(5, 4));
            PlaneLayout plane = new(13, width * 4 + 7, width * 4, height);
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)plane.EndOffset + 5));
            writable.Span.Fill(203);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Span<byte> pixel = writable.Span.Slice(plane.Offset + y * plane.Stride + x * 4, 4);
                pixel[0] = (byte)((31 * x + 79 * y) & 255);
                pixel[1] = (byte)((97 * x + 47 * y) & 255);
                pixel[2] = (byte)((173 * x + 13 * y) & 255);
                pixel[3] = transparent && y == height - 1 && x == width - 1 ? (byte)254 : (byte)255;
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, [plane], new MediaTimestamp(73, new(1, 1000)), new MediaDuration(41, new(1, 1000)));
        }

        private static int Round(decimal value) => Math.Clamp((int)decimal.Floor(value + 0.5m), 0, 255);
    }
}
