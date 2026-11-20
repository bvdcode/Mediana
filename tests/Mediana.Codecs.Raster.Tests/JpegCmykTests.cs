// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class JpegCmykTests
    {
        [Theory]
        [InlineData("cmyk-adobe", JpegColorSpace.InvertedCmyk)]
        [InlineData("cmyk-progressive", JpegColorSpace.InvertedCmyk)]
        [InlineData("cmyk-plain", JpegColorSpace.Cmyk)]
        [InlineData("ycck-multiscan", JpegColorSpace.Ycck)]
        public async Task FourComponentImagesMatchIndependentPixels(string name, JpegColorSpace color)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name + ".jpg"));
            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, name + ".rgba"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame frame = await new JpegImageDecoder().DecodeAsync(encoded, new(), allocator))
            {
                Assert.Equal(37, frame.Format.Width);
                Assert.Equal(29, frame.Format.Height);
                CheckPixels(reference, frame.Data.Span);
                Assert.Equal(reference.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            using (JpegImage image = JpegDecoder.DecodeComponents(encoded, allocator))
            {
                Assert.Equal(4, image.ComponentCount);
                byte[] output = new byte[reference.Length + 4];
                output.AsSpan().Fill(37);
                image.WriteRgbaTo(output, color);
                CheckPixels(reference, output.AsSpan(0, reference.Length));
                Assert.Equal(new byte[] { 37, 37, 37, 37 }, output[^4..]);
                Assert.Throws<ArgumentException>(() => image.WriteRgbaTo(output, JpegColorSpace.YCbCr));
                Assert.Throws<ArgumentException>(() => image.WriteRgbaTo(output.AsSpan(0, reference.Length - 1), color));
                Assert.Throws<OperationCanceledException>(() => image.WriteRgbaTo(output, color, new(true)));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<MediaMemoryLimitException>(() => new JpegImageDecoder().Decode(encoded, new(), new(1)));
            Assert.Throws<InvalidDataException>(() => new JpegImageDecoder().Decode(encoded.AsMemory(0, encoded.Length - 2), new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static void CheckPixels(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.InRange(Math.Abs(expected[index] - actual[index]), 0, (index & 3) == 3 ? 0 : 4);
            }
        }
    }
}
