// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Imaging;

namespace Mediana.Tests
{
    public class RasterContentTests
    {
        [Theory]
        [InlineData("Jpeg/rgb-s0-7x9.jpg", "jpeg.managed.image")]
        [InlineData("Png/c0-d16-i0-13x11.png", "png.managed.image")]
        [InlineData("Raster/opaque.gif", "gif.managed.image")]
        [InlineData("Raster/bitmap-RGB.bmp", "bmp.managed.image")]
        [InlineData("Raster/webp-regions-0.webp", "webp.managed.image")]
        [InlineData("Raster/tiff-L-raw.tif", "tiff.managed.image")]
        [InlineData("Heif/basic-8bit.heic", "heif.managed.image")]
        public async Task FilesDecodeByContentWithMisleadingAndMissingExtensions(string fixture, string implementation)
        {
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", fixture));
            DirectoryInfo directory = Directory.CreateTempSubdirectory();
            try
            {
                ImageDecodeContext registry = ImageResources.DefaultDecoders;
                MediaBufferAllocator allocator = new(64 * 1024 * 1024);
                using VideoFrame reference = await registry.DecodeUsingAsync(implementation, expected, new(), allocator);
                foreach (string name in new[] { "photo.heic", "photo" })
                {
                    string path = Path.Combine(directory.FullName, name);
                    await File.WriteAllBytesAsync(path, expected);
                    byte[] input = await File.ReadAllBytesAsync(path);
                    using VideoFrame frame = await registry.DecodeAsync(input, new(), allocator);
                    Assert.Equal((reference.Format.Width, reference.Format.Height), (frame.Format.Width, frame.Format.Height));
                    Assert.True(reference.Data.Span.SequenceEqual(frame.Data.Span));
                }
            }
            finally { directory.Delete(true); }
        }
    }
}
