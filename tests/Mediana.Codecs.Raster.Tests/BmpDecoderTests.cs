// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Bmp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class BmpDecoderTests
    {
        [Fact]
        public async Task AllBitmapAndIconPixelsMatchIndependentDecoder()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "bmp-manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                IImageDecoder decoder = name.EndsWith(".ico", StringComparison.Ordinal) ? new IcoImageDecoder() : new BmpImageDecoder();
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, entry.GetProperty("pixels").GetString()!));
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame frame = await decoder.DecodeAsync(encoded, new() { PageIndex = entry.TryGetProperty("page", out JsonElement page) ? page.GetInt32() : 0 }, allocator))
                {
                    Assert.Equal(entry.GetProperty("width").GetInt32(), frame.Format.Width);
                    Assert.Equal(entry.GetProperty("height").GetInt32(), frame.Format.Height);
                    Assert.Equal(expected, frame.Data.ToArray());
                    Assert.Equal(frame.Data.Length, allocator.AllocatedBytes);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.True(decoder.CanDecode(encoded));
                Assert.False(decoder.CanDecode([]));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decoder.DecodeAsync(encoded, new(), allocator, new(true)).AsTask());
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

        [Fact]
        public async Task ProbeDoesNotReadBitmapPixelsAndPageSelectionIsValidated()
        {
            byte[] image = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "bitmap-RGB.bmp"));
            byte[] header = image[..54];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), 54);
            Assert.Equal(17, (await new BmpImageDecoder().ReadInfoAsync(header)).Width);
            Assert.Throws<InvalidDataException>(() => new BmpImageDecoder().Decode(header, new(), new(1024 * 1024)));
            Assert.Throws<NotSupportedException>(() => new BmpImageDecoder().Decode(image, new() { PageIndex = 1 }, new(1024 * 1024)));
            Assert.Throws<InvalidDataException>(() => new BmpImageDecoder().Decode(image, new() { MaximumPixels = 1 }, new(1024 * 1024)));
            MediaBufferAllocator budget = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => new BmpImageDecoder().Decode(image, new(), budget));
            Assert.Equal(0, budget.AllocatedBytes);
        }
    }
}
