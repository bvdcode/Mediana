// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Drawing;
namespace Mediana.Tests
{
    internal static class PdfPixelReference
    {
        internal static async Task VerifyAsync(string root, string name, PixelBuffer image, bool exact = false)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = manifest.RootElement.EnumerateArray().Single(x => x.GetProperty("file").GetString() == name + ".rgba.gz");
            if (image.Width != entry.GetProperty("width").GetInt32() || image.Height != entry.GetProperty("height").GetInt32()) { throw new InvalidDataException("PDF reference dimensions differ: " + name); }
            byte[] reference = new byte[image.Pixels.Length];
            await using FileStream input = File.OpenRead(Path.Combine(root, name + ".rgba.gz"));
            await using GZipStream decoder = new(input, CompressionMode.Decompress);
            await decoder.ReadExactlyAsync(reference);
            if (Convert.ToHexStringLower(SHA256.HashData(reference)) != entry.GetProperty("sha256").GetString()) { throw new InvalidDataException("PDF pixel reference is corrupt."); }
            long error = 0, matching = 0;
            for (int i = 0; i < reference.Length; i += 4)
            {
                int maximum = 0;
                for (int c = 0; c < 3; c++) { int delta = Math.Abs(image.Pixels.Span[i + c] - reference[i + c]); error += delta; maximum = Math.Max(maximum, delta); }
                if (image.Pixels.Span[i + 3] != reference[i + 3]) { throw new InvalidDataException("PDF reference alpha differs."); }
                if (maximum <= 8) { matching++; }
            }
            double mae = error / (reference.Length / 4.0 * 3);
            double agreement = matching / (reference.Length / 4.0);
            if (exact && error != 0 || !exact && (mae > 3 || agreement < .95))
            { throw new InvalidDataException($"PDF reference pixels differ: {name}, MAE {mae}, agreement {agreement}."); }
        }
    }
}
