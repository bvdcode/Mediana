// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class PlanarSrgbTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(16)]
        public void YuvPrecisionAndSubsamplingPreserveBlackWhiteAndNeutralGray(int depth)
        {
            foreach ((int horizontal, int vertical) in new[] { (1, 1), (1, 2), (2, 1), (2, 2) })
            {
                PlanarVideoPixelFormat pixels = new("planar", PlanarColorModel.Yuv, depth, horizontal, vertical);
                VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Center);
                MediaBufferAllocator allocator = new(4096);
                using (VideoFrame input = Create(new(3, 3, pixels, color), allocator, (plane, x, _) =>
                    (plane != 0 ? 128 : x switch { 0 => 16, 1 => 235, _ => 126 }) << (depth - 8)))
                using (VideoFrame output = SrgbConverter.Apply(input, allocator))
                {
                    for (int y = 0; y < 3; y++)
                    {
                        ReadOnlySpan<byte> row = output.GetRow(0, y);
                        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 128, 128, 128, 255 }, row.ToArray());
                    }
                }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(16)]
        public void PlanarRgbPreservesChannelOrderAndFullPrecision(int depth)
        {
            PlanarVideoPixelFormat pixels = new("gbr", PlanarColorModel.Gbr, depth);
            int maximum = (1 << depth) - 1; MediaBufferAllocator allocator = new(4096);
            using (VideoFrame input = Create(new(3, 3, pixels, VideoColorInfo.Srgb), allocator, (plane, x, _) => plane == x ? maximum : 0))
            using (VideoFrame output = SrgbConverter.Apply(input, allocator))
            {
                Assert.Equal(new byte[] { 0, 255, 0, 255, 0, 0, 255, 255, 255, 0, 0, 255 }, output.GetRow(0, 0).ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Create(VideoFormat format, MediaBufferAllocator allocator, Func<int, int, int, int> sample)
        {
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate((int)layout[^1].EndOffset);
            int bytes = format.PixelFormat.Planes[0].BytesPerElement;
            for (int plane = 0; plane < 3; plane++)
            {
                PlaneLayout target = layout[plane];
                for (int y = 0; y < target.Height; y++)
                {
                    Span<byte> row = writable.Span.Slice(target.Offset + y * target.Stride, target.RowBytes);
                    for (int x = 0; x < target.RowBytes / bytes; x++)
                    {
                        int value = sample(plane, x, y);
                        if (bytes == 1) { row[x] = (byte)value; } else { MemoryMarshal.Cast<byte, ushort>(row)[x] = (ushort)value; }
                    }
                }
            }
            using MediaBuffer buffer = writable.Publish(); return new(format, buffer, layout);
        }
    }
}
