// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class Yuv420FrameTransformTests
    {
        [Fact]
        public void AllOrthogonalTransformsMatchIndependentContinuousSampleCoordinates()
        {
            foreach ((int a, int b, int c, int d) in new[]
            {
                (1, 0, 0, 1), (-1, 0, 0, 1), (1, 0, 0, -1), (-1, 0, 0, -1),
                (0, 1, 1, 0), (0, 1, -1, 0), (0, -1, 1, 0), (0, -1, -1, 0)
            })
            {
                VideoDisplayMatrix matrix = new(a, b, 0, c, d, 0, 31, -19, 1);
                foreach (ChromaLocation location in new[] { ChromaLocation.Left, ChromaLocation.Center,
                    ChromaLocation.TopLeft, ChromaLocation.Top, ChromaLocation.BottomLeft, ChromaLocation.Bottom })
                {
                    foreach (bool tenBit in new[] { false, true })
                    {
                        foreach ((int width, int height) in new[] { (8, 6), (7, 5), (1, 3) })
                        {
                            MediaBufferAllocator allocator = new(65536);
                            using VideoFrame input = CreateFrame(width, height, tenBit, location, allocator);
                            foreach (VideoPixelFormat target in tenBit
                                ? new[] { VideoPixelFormat.Yuv420P8, VideoPixelFormat.Yuv420P10 }
                                : new[] { VideoPixelFormat.Yuv420P8 })
                            {
                                Yuv420FrameTransform operation = new(input.Format, matrix, target, allocator);
                                using VideoFrame output = operation.Apply(input);
                                bool swapped = a == 0;
                                Assert.Equal(swapped ? height : width, output.Format.Width);
                                Assert.Equal(swapped ? width : height, output.Format.Height);
                                Assert.Equal(swapped ? new(3, 4) : new SampleAspectRatio(4, 3), output.Format.SampleAspectRatio);
                                Assert.Equal(input.Timestamp, output.Timestamp);
                                Assert.Equal(input.Duration, output.Duration);
                                Assert.Equal(input.Format.ColorDescription, output.Format.ColorDescription);
                                Assert.Equal(TransferFunction.Pq, output.Format.Color.Transfer);
                                bool identity = a == 1 && d == 1;
                                Assert.Equal(identity ? location : ChromaLocation.Center, output.Format.Color.ChromaLocation);
                                VerifyPixels(input, output, matrix, location, tenBit, target == VideoPixelFormat.Yuv420P10, identity);
                            }
                            input.Dispose();
                            Assert.Equal(0, allocator.AllocatedBytes);
                        }
                    }
                }
            }
        }

        [Fact]
        public void PrecisionReductionMatchesEveryTenBitCodeAcrossVectorAndScalarTails()
            => YuvFramePreparationTestInput.Verify();

        [Fact]
        public void IdentityRetainsStorageAndFailureDoesNotLeakDestinationStorage()
        {
            MediaBufferAllocator source = new(65536);
            MediaBufferAllocator destination = new(65536);
            using VideoFrame input = CreateFrame(8, 6, true, ChromaLocation.Left, source);
            Yuv420FrameTransform identity = new(input.Format, VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P10, destination);
            using VideoFrame retained = identity.Apply(input);
            Assert.True(retained.Data.Equals(input.Data));
            Assert.Equal(0, destination.AllocatedBytes);
            Yuv420FrameTransform reduce = new(input.Format, VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P8, destination);
            Assert.Throws<OperationCanceledException>(() => reduce.Apply(input, new(true)));
            Assert.Equal(0, destination.AllocatedBytes);
            Assert.Throws<MediaMemoryLimitException>(() => new Yuv420FrameTransform(input.Format,
                VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P8, new(1)).Apply(input));
            input.Dispose();
            using VideoFrame output = reduce.Apply(retained);
            Assert.NotEmpty(output.Data.ToArray());
            output.Dispose(); retained.Dispose();
            Assert.Equal(0, destination.AllocatedBytes);
            Assert.Equal(0, source.AllocatedBytes);
        }

        [Fact]
        public void EquivalentColorDescriptionsSharePixelsButChangedInterpretationsAreRejected()
        {
            MediaBufferAllocator allocator = new(65536);
            using VideoFrame input = CreateFrame(8, 6, false, ChromaLocation.Center, allocator);
            VideoFormat declared = new(8, 6, VideoPixelFormat.Yuv420P8, input.Format.Color, input.Format.SampleAspectRatio);
            Yuv420FrameTransform operation = new(declared, VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P8, allocator);
            using VideoFrame result = operation.Apply(input);
            Assert.Same(operation.OutputFormat, result.Format);
            Assert.Equal(input.Format.GetColorInterpretation(), result.Format.GetColorInterpretation());
            Assert.True(input.Data.Equals(result.Data));
            VideoFormat changed = new(8, 6, VideoPixelFormat.Yuv420P8,
                input.Format.Color with { Transfer = TransferFunction.Bt709 }, input.Format.SampleAspectRatio);
            Assert.Throws<NotSupportedException>(() => new Yuv420FrameTransform(changed,
                VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P8, allocator).Apply(input));
        }

        [Fact]
        public void InvalidSampleValuesReleasePartialOutputAndUnknownSitingIsNotGuessed()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = new(64, 4, VideoPixelFormat.Yuv420P10);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset);
            writable.Memory.Span.Fill(255);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame input = new(format, buffer, planes);
            Yuv420FrameTransform reduce = new(format, VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P8, allocator);
            long held = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => reduce.Apply(input));
            Assert.Equal(held, allocator.AllocatedBytes);
            Assert.Throws<NotSupportedException>(() => new Yuv420FrameTransform(format,
                new(-1, 0, 0, 0, 1, 0, 0, 0, 1), VideoPixelFormat.Yuv420P8, allocator));
            Assert.Throws<NotSupportedException>(() => new Yuv420FrameTransform(
                new(4, 4, VideoPixelFormat.Yuv420P8), VideoDisplayMatrix.Identity, VideoPixelFormat.Yuv420P10, allocator));
        }

        private static VideoFrame CreateFrame(int width, int height, bool tenBit, ChromaLocation location,
            MediaBufferAllocator allocator)
        {
            VideoFormat format = new(width, height, tenBit ? VideoPixelFormat.Yuv420P10 : VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Pq, MatrixCoefficients.Bt709, ColorRange.Limited, location),
                new(4, 3), new CicpColorDescription(1, 16, 1));
            PlaneLayout[] planes = format.CreateTightLayout();
            int offset = 7;
            for (int plane = 0; plane < planes.Length; plane++)
            {
                planes[plane] = new(offset, planes[plane].Stride + 6, planes[plane].RowBytes, planes[plane].Height);
                offset = (int)planes[plane].EndOffset + 3;
            }
            using WritableMediaBuffer writable = allocator.Allocate(offset);
            writable.Memory.Span.Fill(217);
            for (int plane = 0; plane < planes.Length; plane++)
            {
                for (int y = 0; y < planes[plane].Height; y++)
                {
                    Span<byte> row = writable.Memory.Span.Slice(planes[plane].Offset + y * planes[plane].Stride, planes[plane].RowBytes);
                    for (int x = 0; x < row.Length / (tenBit ? 2 : 1); x++)
                    {
                        int value = (x * 127 + y * 91 + plane * 73) % (tenBit ? 1024 : 256);
                        if (tenBit) { MemoryMarshal.Cast<byte, ushort>(row)[x] = (ushort)value; }
                        else { row[x] = (byte)value; }
                    }
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes, new(19, new(1, 30000)), new(1001, new(1, 30000)));
        }

        private static void VerifyPixels(VideoFrame input, VideoFrame output, VideoDisplayMatrix matrix,
            ChromaLocation location, bool inputTenBit, bool outputTenBit, bool identity)
        {
            (int horizontal, int vertical) = ChromaSamplePosition.InHalfLumaSamples(location);
            decimal minimumX = Math.Min(0, (input.Format.Width - 1) * (decimal)matrix.M11)
                + Math.Min(0, (input.Format.Height - 1) * (decimal)matrix.M21);
            decimal minimumY = Math.Min(0, (input.Format.Width - 1) * (decimal)matrix.M12)
                + Math.Min(0, (input.Format.Height - 1) * (decimal)matrix.M22);
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < output.Planes[plane].Height; y++)
                {
                    for (int x = 0; x < output.Planes[plane].RowBytes / (outputTenBit ? 2 : 1); x++)
                    {
                        decimal value;
                        if (identity) { value = Read(input, plane, x, y, inputTenBit); }
                        else
                        {
                            decimal displayX = (plane == 0 ? x : 2 * x + 0.5m) + minimumX;
                            decimal displayY = (plane == 0 ? y : 2 * y + 0.5m) + minimumY;
                            decimal sourceX = displayX * (decimal)matrix.M11 + displayY * (decimal)matrix.M12;
                            decimal sourceY = displayX * (decimal)matrix.M21 + displayY * (decimal)matrix.M22;
                            value = plane == 0 ? Read(input, plane, (int)sourceX, (int)sourceY, inputTenBit)
                                : Interpolate(input, plane, (sourceX - horizontal / 2m) / 2,
                                    (sourceY - vertical / 2m) / 2, inputTenBit);
                        }
                        if (inputTenBit && !outputTenBit) { value /= 4; }
                        int expected = Math.Min(outputTenBit ? 1023 : 255, (int)decimal.Round(value, 0, MidpointRounding.AwayFromZero));
                        Assert.Equal(expected, Read(output, plane, x, y, outputTenBit));
                    }
                }
            }
        }

        private static decimal Interpolate(VideoFrame input, int plane, decimal x, decimal y, bool tenBit)
        {
            int left = (int)decimal.Floor(x);
            int top = (int)decimal.Floor(y);
            decimal dx = x - left;
            decimal dy = y - top;
            return Read(input, plane, left, top, tenBit) * (1 - dx) * (1 - dy)
                + Read(input, plane, left + 1, top, tenBit) * dx * (1 - dy)
                + Read(input, plane, left, top + 1, tenBit) * (1 - dx) * dy
                + Read(input, plane, left + 1, top + 1, tenBit) * dx * dy;
        }

        private static int Read(VideoFrame input, int plane, int x, int y, bool tenBit)
        {
            x = Math.Clamp(x, 0, input.Planes[plane].RowBytes / (tenBit ? 2 : 1) - 1);
            y = Math.Clamp(y, 0, input.Planes[plane].Height - 1);
            ReadOnlySpan<byte> row = input.GetRow(plane, y);
            return tenBit ? MemoryMarshal.Cast<byte, ushort>(row)[x] : row[x];
        }
    }
}
