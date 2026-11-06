// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class WebpDecoderTests
    {
        [Fact]
        public async Task MetadataDoesNotDecodeSamplesAndFramesOwnTheirPixels()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "webp-text-4.webp"));
            WebpImageDecoder decoder = new();
            ImageInfo info = await decoder.ReadInfoAsync(encoded);
            Assert.Contains(info.Metadata.Properties, property => property.Namespace == "icc");
            Assert.Contains(info.Metadata.Properties, property => property.Namespace == "xmp");
            Assert.Single(info.Metadata.Children);
            byte[] headerOnly = new byte[26];
            "RIFF"u8.CopyTo(headerOnly); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(headerOnly.AsSpan(4), 18);
            "WEBPVP8L"u8.CopyTo(headerOnly.AsSpan(8));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(headerOnly.AsSpan(16), 5);
            headerOnly[20] = 47;
            Assert.Equal(1, (await decoder.ReadInfoAsync(headerOnly)).Width);
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<InvalidDataException>(() => decoder.Decode(headerOnly, new(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            encoded = await File.ReadAllBytesAsync(Path.Combine(root, "webp-animation.webp"));
            await using IAsyncEnumerator<VideoFrame> frames = decoder.DecodeFramesAsync(encoded, new(), allocator).GetAsyncEnumerator();
            Assert.True(await frames.MoveNextAsync());
            using VideoFrame opening = frames.Current;
            byte[] copy = opening.Data.ToArray();
            while (await frames.MoveNextAsync())
            {
                using VideoFrame next = frames.Current;
                Assert.True(copy.AsSpan().SequenceEqual(opening.Data.Span));
            }
            opening.Dispose(); Assert.Equal(0, allocator.AllocatedBytes);
        }
        [Fact]
        public async Task IndependentLosslessImagesAndAnimationMatchEveryPixel()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "webp-manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                MediaBufferAllocator allocator = new(16 * 1024 * 1024); WebpImageDecoder decoder = new();
                int index = 0;
                await foreach (VideoFrame frame in decoder.DecodeFramesAsync(encoded, new(), allocator))
                {
                    using (frame)
                    {
                        byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, entry.GetProperty("pixels")[index++].GetString()!));
                        Assert.Equal(entry.GetProperty("width").GetInt32(), frame.Format.Width);
                        Assert.Equal(entry.GetProperty("height").GetInt32(), frame.Format.Height);
                        int mismatch = -1; bool lossy = entry.TryGetProperty("lossy", out JsonElement lossyValue) && lossyValue.GetBoolean();
                        int maximum = 0; long squares = 0;
                        for (int byteIndex = 0; byteIndex < expected.Length; byteIndex++)
                        { int error = Math.Abs(expected[byteIndex] - frame.Data.Span[byteIndex]);
                            maximum = Math.Max(maximum, error); squares += error * error;
                            if (error > (lossy && byteIndex % 4 != 3 ? 4 : 0) && mismatch < 0) { mismatch = byteIndex; } }
                        Assert.True(mismatch < 0, $"{name}, frame {index - 1}, byte {mismatch}: expected {(mismatch < 0 ? 0 : expected[mismatch])}, actual {(mismatch < 0 ? 0 : frame.Data.Span[mismatch])}; max={maximum}, rms={Math.Sqrt((double)squares / expected.Length)}.");
                    }
                }
                Assert.Equal(entry.GetProperty("pixels").GetArrayLength(), index);
                Assert.Equal(0, allocator.AllocatedBytes);
                using (VideoFrame still = await decoder.DecodeAsync(encoded, new(), allocator))
                { Assert.Equal(still.Data.Length, allocator.AllocatedBytes); }
                Assert.Equal(0, allocator.AllocatedBytes);
                ImageInfo info = await decoder.ReadInfoAsync(encoded);
                Assert.Equal(entry.GetProperty("width").GetInt32(), info.Width);
                Assert.ThrowsAny<OperationCanceledException>(() => decoder.Decode(encoded, new(), allocator, new(true)));
                if (info.Width * info.Height > 1) { Assert.Throws<InvalidDataException>(() => decoder.Decode(encoded, new() { MaximumPixels = 1 }, allocator)); }
            }
        }
    }
}
