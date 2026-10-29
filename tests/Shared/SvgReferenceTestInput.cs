// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Drawing;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    internal static class SvgReferenceTestInput
    {
        public static async Task VerifyAllAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "filter-reference.json")));
            foreach (JsonElement entry in manifest.RootElement.GetProperty("Cases").EnumerateArray())
            {
                SvgDocument document = SvgReader.Read(entry.GetProperty("Svg").GetString()!);
                PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions { Supersampling = 1 });
                if (document.UnsupportedFeatures.Count != 0 ||
                    Convert.ToHexStringLower(SHA256.HashData(image.Pixels.Span)) != entry.GetProperty("Sha256").GetString())
                {
                    throw new InvalidDataException("SVG reference pixels differ: " + entry.GetProperty("Name").GetString());
                }
            }
            SvgDocument text = SvgReader.Read("<svg width='160' height='30'><text x='2' y='23'>Hello Мир</text></svg>");
            PixelBuffer rendered = SvgRenderer.Render(text, new SvgRenderOptions { Supersampling = 1 });
            if (!rendered.Pixels.Span.ContainsAnyExcept((byte)0))
            {
                throw new InvalidDataException("Bundled font rendering returned an empty image.");
            }
        }
    }
}
