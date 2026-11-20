// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class RgbaResizeReferenceTests
    {
        [Theory]
        [InlineData(17, 13, 5, 3)]
        [InlineData(5, 3, 17, 13)]
        [InlineData(17, 3, 5, 13)]
        [InlineData(1, 13, 9, 1)]
        public void SeparableFilterMatchesDirectDoublePrecisionConvolution(int width, int height, int targetWidth, int targetHeight)
        {
            byte[] pixels = new byte[width * height * 4];
            new Random(31).NextBytes(pixels);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame input = RgbaFrame.CopyFrom(pixels, width, height, VideoColorInfo.Srgb, allocator);
            foreach (ImageResizeFilter filter in new[] { ImageResizeFilter.Bilinear, ImageResizeFilter.Bicubic, ImageResizeFilter.Lanczos3 })
            {
                foreach (ResizeColorSpace space in Enum.GetValues<ResizeColorSpace>())
                {
                    using VideoFrame output = RgbaResize.Apply(input, targetWidth, targetHeight, allocator, new() { Filter = filter, ColorSpace = space });
                    for (int y = 0; y < targetHeight; y++)
                    {
                        for (int x = 0; x < targetWidth; x++)
                        {
                            byte[] expected = Reference(pixels, width, height, targetWidth, targetHeight, x, y, filter, space);
                            ReadOnlySpan<byte> actual = output.GetRow(0, y).Slice(x * 4, 4);
                            for (int c = 0; c < 4; c++) { Assert.InRange(Math.Abs(actual[c] - expected[c]), 0, 1); }
                        }
                    }
                }
            }
        }

        private static byte[] Reference(byte[] pixels, int width, int height, int targetWidth, int targetHeight,
            int x, int y, ImageResizeFilter filter, ResizeColorSpace space)
        {
            double[] sum = new double[4];
            double total = 0;
            for (int sy = 0; sy < height; sy++)
            {
                double wy = Weight(filter, (sy + 0.5 - (y + 0.5) * height / targetHeight) / Math.Max(1, (double)height / targetHeight));
                for (int sx = 0; sx < width; sx++)
                {
                    double weight = wy * Weight(filter, (sx + 0.5 - (x + 0.5) * width / targetWidth) / Math.Max(1, (double)width / targetWidth));
                    total += weight;
                    int offset = (sy * width + sx) * 4;
                    double alpha = pixels[offset + 3] / 255.0;
                    sum[3] += alpha * weight;
                    for (int c = 0; c < 3; c++)
                    {
                        double value = pixels[offset + c] / 255.0;
                        if (space == ResizeColorSpace.LinearSrgb) { value = value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
                        sum[c] += value * alpha * weight;
                    }
                }
            }
            byte[] result = new byte[4];
            result[3] = ToByte(sum[3] / total);
            if (result[3] == 0) { return result; }
            for (int c = 0; c < 3; c++)
            {
                double value = Math.Clamp(sum[c] / sum[3], 0, 1);
                if (space == ResizeColorSpace.LinearSrgb) { value = value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055; }
                result[c] = ToByte(value);
            }
            return result;
        }

        private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255 + 0.5);
        private static double Weight(ImageResizeFilter filter, double distance)
        {
            double x = Math.Abs(distance);
            switch (filter)
            {
                case ImageResizeFilter.Bilinear:
                    return Math.Max(0, 1 - x);
                case ImageResizeFilter.Bicubic:
                    if (x <= 1) { return 1 - 2.5 * x * x + 1.5 * x * x * x; }
                    return x < 2 ? 2 - 4 * x + 2.5 * x * x - 0.5 * x * x * x : 0;
                case ImageResizeFilter.Lanczos3:
                    if (x == 0) { return 1; }
                    return x < 3 ? 3 * Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / 3) / (Math.PI * Math.PI * x * x) : 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(filter));
            }
        }
    }
}
