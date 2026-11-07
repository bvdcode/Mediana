// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using System.Buffers.Binary;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class WebpDecoderTests
    {
        [Fact]
        public async Task OddFinalVp8PayloadUsesContainerPaddingAndRejectsInvalidExtents()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "webp-vp8-odd-padding.webp"));
            Assert.Equal(506, encoded.Length);
            Assert.Equal((uint)498, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(4)));
            Assert.True(encoded.AsSpan(222, 4).SequenceEqual("VP8 "u8));
            Assert.Equal((uint)275, BinaryPrimitives.ReadUInt32LittleEndian(encoded.AsSpan(226)));
            Assert.Equal(0, encoded[^1]);
            WebpImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
            { Assert.Equal(33, frame.Format.Width); Assert.Equal(35, frame.Format.Height); }
            Assert.Equal(0, allocator.AllocatedBytes);
            byte[] badPadding = encoded.ToArray(); badPadding[^1] = 1;
            await RejectAsync(badPadding);
            byte[] missingPadding = encoded[..^1];
            BinaryPrimitives.WriteInt32LittleEndian(missingPadding.AsSpan(4), missingPadding.Length - 8);
            await RejectAsync(missingPadding);
            byte[] badSize = encoded.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(badSize.AsSpan(226), uint.MaxValue);
            await RejectAsync(badSize);

            async Task RejectAsync(byte[] data)
            {
                await Assert.ThrowsAsync<InvalidDataException>(async () => await decoder.DecodeAsync(data, new(), allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }

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
                        {
                            int error = Math.Abs(expected[byteIndex] - frame.Data.Span[byteIndex]);
                            maximum = Math.Max(maximum, error); squares += error * error;
                            if (error > (lossy && byteIndex % 4 != 3 ? 4 : 0) && mismatch < 0) { mismatch = byteIndex; }
                        }
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
