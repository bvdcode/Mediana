// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Bmp;
using Mediana.Codecs.Gif;
using Mediana.Codecs.Tiff;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
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
            byte[] gif = await File.ReadAllBytesAsync(Path.Combine(root, "opaque.gif"));
            MediaBufferAllocator frames = new(16 * 1024 * 1024);
            await foreach (VideoFrame frame in new GifImageDecoder().DecodeFramesAsync(gif, new(), frames)) { frame.Dispose(); }
            if (frames.AllocatedBytes != 0) { throw new InvalidDataException("GIF sequence retained storage."); }
            Console.WriteLine($"PASS raster decoding: {verified} BMP/ICO, TIFF and WebP cases match independent pixels; GIF animation releases all frames.");
        }
    }
}
