// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs.Tiff;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class TiffDecoderTests
    {
        [Fact]
        public async Task CompressionByteOrdersPlanesTilesAndPagesMatchIndependentPixels()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "tiff-manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                byte[] data = await File.ReadAllBytesAsync(Path.Combine(root, name));
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, entry.GetProperty("pixels").GetString()!));
                TiffImageDecoder decoder = new(); MediaBufferAllocator allocator = new(2 * 1024 * 1024);
                using (VideoFrame frame = await decoder.DecodeAsync(data, new() { PageIndex = entry.GetProperty("page").GetInt32() }, allocator))
                {
                    int maximumError = 0;
                    long squaredError = 0;
                    for (int index = 0; index < reference.Length; index++)
                    {
                        int error = reference[index] - frame.Data.Span[index];
                        maximumError = Math.Max(maximumError, Math.Abs(error)); squaredError += error * error;
                    }
                    double rms = Math.Sqrt((double)squaredError / reference.Length);
                    bool jpeg = name.Contains("-jpeg", StringComparison.Ordinal);
                    Assert.True(jpeg ? maximumError <= 4 && rms < 1 : maximumError == 0, $"{name}: maximum error {maximumError}, RMS {rms}.");
                    Assert.Equal(entry.GetProperty("width").GetInt32(), frame.Format.Width);
                    Assert.Equal(entry.GetProperty("height").GetInt32(), frame.Format.Height);
                    Assert.Equal(reference.Length, allocator.AllocatedBytes);
                }
                Assert.Equal(0, allocator.AllocatedBytes);
                ImageInfo info = await decoder.ReadInfoAsync(data);
                Assert.NotEmpty(info.Metadata.Children);
                Assert.True(decoder.CanDecode(data));
                Assert.Throws<InvalidDataException>(() => decoder.Decode(data, new() { MaximumPixels = 1 }, allocator));
                Assert.ThrowsAny<OperationCanceledException>(() => decoder.Decode(data, new(), allocator, new(true)));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
    }
}
