// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class PlanarAlphaTests
    {
        [Theory]
        [InlineData(8, 16)]
        [InlineData(12, 8)]
        [InlineData(12, 16)]
        public void AlphaPrecisionIsIndependentFromColorPrecision(int colorDepth, int alphaDepth)
        {
            PlanarVideoPixelFormat pixels = new("gbra", PlanarColorModel.Gbr, colorDepth, 1, 1, alphaDepth);
            Assert.Equal(pixels, new PlanarVideoPixelFormat("same", PlanarColorModel.Gbr, colorDepth, 1, 1, alphaDepth));
            Assert.NotEqual(pixels, new PlanarVideoPixelFormat("opaque", PlanarColorModel.Gbr, colorDepth));
            MediaBufferAllocator allocator = new(4096);
            int maximum = (1 << alphaDepth) - 1;
            using (VideoFrame input = PlanarSrgbTests.Create(new(3, 2, pixels, VideoColorInfo.Srgb), allocator,
                (plane, x, _) => plane == 3 ? x * maximum / 2 : (1 << colorDepth) - 1))
            using (VideoFrame output = SrgbConverter.Apply(input, allocator))
            {
                Assert.Equal(new byte[] { 255, 255, 255, 0, 255, 255, 255, 127, 255, 255, 255, 255 }, output.GetRow(0, 0).ToArray());
                Assert.Throws<NotSupportedException>(() => new RgbaToYuv420(input.Format, new(), allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FieldInterpolationUsesAlphaWordPrecision()
        {
            MediaBufferAllocator allocator = new(4096);
            PlanarVideoPixelFormat pixels = new("gbra", PlanarColorModel.Gbr, 8, 1, 1, 16);
            using (VideoFrame input = PlanarSrgbTests.Create(new(1, 2, pixels, null, null, null, VideoScanMode.TopField), allocator,
                (plane, _, y) => plane == 3 ? y * 65535 : 128))
            using (VideoFrame output = VideoFieldInterpolator.Apply(input, 3, allocator))
            {
                Assert.Equal(32768, MemoryMarshal.Cast<byte, ushort>(output.GetRow(3, 1))[0]);
                Assert.Equal(128, output.GetRow(0, 1)[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
