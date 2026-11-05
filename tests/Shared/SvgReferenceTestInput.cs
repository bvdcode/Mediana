// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Drawing;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    internal static class SvgReferenceTestInput
    {
        public static async Task VerifyAllAsync(string root)
        {
            VerifyEmbeddedImages();
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "filter-reference.json")));
            foreach (JsonElement entry in manifest.RootElement.GetProperty("Cases").EnumerateArray())
            {
                using SvgDocument document = SvgReader.Read(entry.GetProperty("Svg").GetString()!);
                using PixelBuffer image = SvgRenderer.Render(document, new DrawingRenderOptions { Supersampling = 1 });
                if (document.UnsupportedFeatures.Count != 0 ||
                    Convert.ToHexStringLower(SHA256.HashData(image.Pixels.Span)) != entry.GetProperty("Sha256").GetString())
                {
                    throw new InvalidDataException("SVG reference pixels differ: " + entry.GetProperty("Name").GetString());
                }
            }
            using SvgDocument text = SvgReader.Read("<svg width='160' height='30'><text x='2' y='23'>Hello Мир</text></svg>");
            using PixelBuffer rendered = SvgRenderer.Render(text, new DrawingRenderOptions { Supersampling = 1 });
            if (!rendered.Pixels.Span.ContainsAnyExcept((byte)0))
            {
                throw new InvalidDataException("Bundled font rendering returned an empty image.");
            }
        }
        internal static void VerifyEmbeddedImages()
        {
            using PixelBuffer input = new(2, 1);
            input[0, 0] = new(255, 0, 0);
            input[1, 0] = new(0, 255, 0);
            MediaBufferAllocator sourceBudget = new(1024);
            using VideoFrame source = input.ToVideoFrame(sourceBudget);
            using MediaBuffer png = PngEncoder.Encode(source, sourceBudget);
            string uri = "data:image/png;base64," + Convert.ToBase64String(png.Memory.Span);
            MediaBufferAllocator images = new(24);
            using SvgDocument document = SvgReader.Read("<svg width='4' height='1'><image href='" + uri + "' width='2' height='1'/><image x='2' href='" + uri + "' width='2' height='1'/></svg>",
                new() { ImageAllocator = images });
            if (images.AllocatedBytes != 8) { throw new InvalidDataException("SVG retained more than one decoded PNG image."); }
            using PixelBuffer output = SvgRenderer.Render(document, new() { Supersampling = 1 });
            if (output[0, 0] != input[0, 0] || output[1, 0] != input[1, 0] || output[2, 0] != input[0, 0] || output[3, 0] != input[1, 0])
            { throw new InvalidDataException("Shared PNG decoder SVG pixels differ."); }
            document.Dispose();
            if (images.AllocatedBytes != 0) { throw new InvalidDataException("SVG image disposal retained image storage."); }
        }
    }
}
