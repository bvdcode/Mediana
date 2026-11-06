// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Bmp;
using Mediana.Codecs.Gif;
using Mediana.Codecs.Tiff;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.NativeTests
{
    internal static class NativeRasterTests
    {
        internal static async Task RunAsync()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Raster");
            ImageDecodeContext registry = new([new BmpImageDecoder(), new IcoImageDecoder(), new GifImageDecoder(), new TiffImageDecoder(), new WebpImageDecoder()]);
            int verified = 0;
            foreach (string family in new[] { "bmp", "tiff", "webp" })
            {
                using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, family + "-manifest.json")));
                foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
                {
                    string name = entry.GetProperty("file").GetString()!;
                    byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                    JsonElement pixels = entry.GetProperty("pixels");
                    string referenceName = (pixels.ValueKind == JsonValueKind.Array ? pixels[0] : pixels).GetString()!;
                    byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, referenceName));
                    int page = entry.TryGetProperty("page", out JsonElement selected) ? selected.GetInt32() : 0;
                    MediaBufferAllocator allocator = new(16 * 1024 * 1024);
                    using (VideoFrame frame = await registry.DecodeAsync(encoded, new() { PageIndex = page }, allocator))
                    {
                        if (frame.Data.Length != reference.Length) { throw new InvalidDataException("Raster dimensions differ: " + name); }
                        int tolerance = entry.TryGetProperty("maximum_error", out JsonElement error) ? error.GetInt32() : 0;
                        for (int index = 0; index < reference.Length; index++)
                        {
                            if (Math.Abs(reference[index] - frame.Data.Span[index]) > tolerance) { throw new InvalidDataException("Raster pixels differ: " + name); }
                        }
                    }
                    if (allocator.AllocatedBytes != 0) { throw new InvalidDataException("Raster decoding retained storage: " + name); }
                    verified++;
                    if (pixels.ValueKind == JsonValueKind.Array)
                    {
                        int frameIndex = 0;
                        await foreach (VideoFrame animationFrame in registry.DecodeFramesAsync(encoded, new(), allocator))
                        {
                            using (animationFrame)
                            {
                                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, pixels[frameIndex++].GetString()!));
                                int tolerance = entry.TryGetProperty("maximum_error", out JsonElement error) ? error.GetInt32() : 0;
                                for (int index = 0; index < expected.Length; index++)
                                {
                                    if (Math.Abs(expected[index] - animationFrame.Data.Span[index]) > (index % 4 == 3 ? 0 : tolerance))
                                    { throw new InvalidDataException("WebP sequence pixels differ: " + name); }
                                }
                            }
                        }
                        if (frameIndex != pixels.GetArrayLength() || allocator.AllocatedBytes != 0)
                        { throw new InvalidDataException("WebP sequence count or ownership differs: " + name); }
                    }
                }
            }
            await VerifyGifAsync(root, registry);
            await VerifyRawMetadataAsync(root);
            Console.WriteLine($"PASS raster decoding: {verified} BMP/ICO, TIFF and WebP cases, complete GIF/WebP sequences and TIFF-based RAW metadata.");
        }
        private static async Task VerifyGifAsync(string root, ImageDecodeContext registry)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "gif-manifest.json")));
            foreach (IGrouping<string, JsonElement> group in manifest.RootElement.EnumerateArray().GroupBy(entry => entry.GetProperty("file").GetString()!))
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, group.Key));
                JsonElement[] entries = group.ToArray();
                MediaBufferAllocator allocator = new(16 * 1024 * 1024);
                int frameIndex = 0;
                await foreach (VideoFrame frame in registry.DecodeFramesAsync(encoded, new(), allocator))
                {
                    using (frame)
                    {
                        JsonElement entry = entries[frameIndex++];
                        byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, entry.GetProperty("pixels").GetString()!));
                        if (frame.Data.Length != reference.Length || frame.Duration!.Value.Value * 10 != entry.GetProperty("duration").GetInt32())
                        {
                            throw new InvalidDataException("GIF frame dimensions or duration differ: " + group.Key);
                        }
                        for (int offset = 0; offset < reference.Length; offset += 4)
                        {
                            if (frame.Data.Span[offset + 3] != reference[offset + 3] ||
                                (reference[offset + 3] != 0 && !frame.Data.Span.Slice(offset, 3).SequenceEqual(reference.AsSpan(offset, 3))))
                            {
                                throw new InvalidDataException("GIF visible pixels differ: " + group.Key);
                            }
                        }
                    }
                }
                if (frameIndex != entries.Length || allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("GIF sequence count or ownership differs: " + group.Key);
                }
            }
        }

        private static async Task VerifyRawMetadataAsync(string root)
        {
            foreach (string family in new[] { "dng", "cr2", "nef", "arw", "tiff" })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, $"raw-metadata-{family}.bin"));
                ImageInfo info = await new TiffImageDecoder().ReadInfoAsync(encoded);
                bool raw = family != "tiff";
                if (info.FormatId != family || info.Width != (raw ? 4000 : 160) || info.Height != (raw ? 6000 : 120) || info.Metadata.Children.Count == 0)
                {
                    throw new InvalidDataException("TIFF-based RAW metadata differs: " + family);
                }
            }
        }
    }
}
