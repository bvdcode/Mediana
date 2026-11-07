// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class JpegStorageTests
    {
        [Fact]
        public async Task SequentialAndProgressiveComponentsRetainOneSampleRentalPerImage()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Jpeg");
            foreach (string file in Directory.EnumerateFiles(root, "*.jpg"))
            {
                byte[] encoded = await File.ReadAllBytesAsync(file);
                using CountingMemoryPool pool = new();
                MediaBufferAllocator allocator = new(8 * 1024 * 1024, pool);
                using (JpegImage image = JpegDecoder.DecodeComponents(encoded, allocator))
                {
                    Assert.Equal(1, pool.Rentals - pool.Returns);
                    Assert.True(allocator.AllocatedBytes >= image.Width * image.Height);
                    byte[] first = new byte[image.Width * image.Height * 4];
                    byte[] second = new byte[first.Length];
                    JpegColorSpace color = image.AdobeColorTransform switch
                    {
                        0 => JpegColorSpace.Rgb,
                        null or 1 => JpegColorSpace.YCbCr,
                        _ => throw new InvalidDataException("Unexpected JPEG corpus component transform.")
                    };
                    image.WriteRgbaTo(first, color);
                    encoded.AsSpan().Clear();
                    image.WriteRgbaTo(second, color);
                    Assert.Equal(first, second);
                }
                Assert.Equal(pool.Rentals, pool.Returns);
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
    }
}
