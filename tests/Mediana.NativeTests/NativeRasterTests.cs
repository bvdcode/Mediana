// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs;
using Mediana.Codecs.Tga;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Pnm;
using Mediana.Codecs.Qoi;
using Mediana.Codecs.Bmp;
using Mediana.Codecs.Gif;
using Mediana.Codecs.Png;
using Mediana.Codecs.Tiff;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.NativeTests
{
    internal static class NativeRasterTests
    {
        internal static async Task RunAsync()
        {
            await NativeJpegEncodingTests.RunAsync();
            await NativeOpenExrTests.RunAsync();
            string root = Path.Combine(AppContext.BaseDirectory, "Data", "Raster");
            ImageDecodeContext registry = new([new PngImageDecoder(), new BmpImageDecoder(), new IcoImageDecoder(), new AniImageDecoder(), new GifImageDecoder(), new TiffImageDecoder(), new WebpImageDecoder(), new QoiImageDecoder(), new PnmImageDecoder(), new TgaImageDecoder()]);
            int verified = 0;
            foreach (string family in new[] { "bmp", "tiff", "webp", "qoi", "pnm", "tga" })
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
            await VerifyCmykAsync(root);
            await VerifyPnmColorAsync();
            await VerifyAniAsync(root, registry);
            await VerifyRawMetadataAsync(root);
            await NativeImageInspectionTests.RunAsync(root);
            await Mediana.Tests.IccColorTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Icc"));
            Console.WriteLine($"PASS raster decoding: {verified} BMP/ICO, TIFF and WebP cases, complete GIF/WebP sequences and TIFF-based RAW metadata.");
        }

        private static async Task VerifyPnmColorAsync()
        {
            byte[] encoded = [.. "P5\n3 1\n255\n"u8, 8, 128, 255];
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame source = await new PnmImageDecoder().DecodeAsync(encoded, new(), allocator))
            using (VideoFrame converted = SrgbConverter.ApplyWithColorPolicy(source, null, allocator))
            {
                byte[] expected = [20, 20, 20, 255, 140, 140, 140, 255, 255, 255, 255, 255];
                if (!converted.GetRow(0, 0).SequenceEqual(expected))
                {
                    throw new InvalidDataException("PNM optical transfer differs from the sRGB reference.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("PNM color conversion retained storage.");
            }
        }

        private static async Task VerifyCmykAsync(string root)
        {
            foreach (string name in new[] { "cmyk-adobe.jpg", "cmyk-progressive.jpg", "cmyk-plain.jpg", "ycck-multiscan.jpg",
                "rgb-jpeg-regression.tif", "l-jpeg-regression.tif", "cmyk-jpeg-regression.tif", "ycbcr-jpeg-exif.tif" })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, name));
                byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, Path.ChangeExtension(name, ".rgba")));
                IImageDecoder decoder = Path.GetExtension(name) switch
                {
                    ".jpg" => new JpegImageDecoder(),
                    ".tif" => new TiffImageDecoder(),
                    _ => throw new InvalidDataException("Invalid CMYK reference image.")
                };
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
                {
                    if (frame.Format.Width != 37 || frame.Format.Height != 29 || frame.Data.Length != reference.Length)
                    {
                        throw new InvalidDataException("JPEG component image dimensions differ: " + name);
                    }
                    for (int index = 0; index < reference.Length; index++)
                    {
                        if (Math.Abs(reference[index] - frame.Data.Span[index]) > ((index & 3) == 3 ? 0 : 4))
                        {
                            throw new InvalidDataException("JPEG component image pixels differ: " + name);
                        }
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("JPEG component decoding retained storage: " + name);
                }
            }
        }

        private static async Task VerifyAniAsync(string root, ImageDecodeContext registry)
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "ani-sequence.ani"));
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            int step = 0;
            int[] durations = [2, 3, 4];
            long[] times = [0, 2, 5];
            byte[] png = await File.ReadAllBytesAsync(Path.Combine(root, "icon-png-0.rgba"));
            byte[] dib = await File.ReadAllBytesAsync(Path.Combine(root, "icon-bmp-0.rgba"));
            await foreach (VideoFrame frame in registry.DecodeFramesAsync(encoded, new(), allocator))
            {
                using (frame)
                {
                    if (step >= 3 || frame.Timestamp?.Value != times[step] || frame.Duration?.Value != durations[step])
                    {
                        throw new InvalidDataException("ANI sequence timing differs.");
                    }
                    if (!frame.Data.Span.SequenceEqual(step == 1 ? dib : png)) { throw new InvalidDataException("ANI frame pixels differ."); }
                    step++;
                }
            }
            if (step != 3 || allocator.AllocatedBytes != 0) { throw new InvalidDataException("ANI sequence count or ownership differs."); }
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
