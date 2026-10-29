// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Jbig2;

namespace Mediana.Tests
{
    internal static class Jbig2ReferenceTestInput
    {
        public static async Task VerifyAllAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("name").GetString()!;
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name + ".jb2"));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".bin"));
                if (Convert.ToHexStringLower(SHA256.HashData(input)) != entry.GetProperty("sha256").GetString() ||
                    Convert.ToHexStringLower(SHA256.HashData(expected)) != entry.GetProperty("pixelSha256").GetString())
                {
                    throw new InvalidDataException("JBIG2 fixture checksum mismatch: " + name);
                }
                Jbig2Document document = Jbig2Decoder.Decode(input);
                if (document.Pages.Count != 1 || document.Pages[0].Width != entry.GetProperty("width").GetInt32() ||
                    document.Pages[0].Height != entry.GetProperty("height").GetInt32() ||
                    !document.Pages[0].Data.Span.SequenceEqual(expected))
                {
                    throw new InvalidDataException("JBIG2 pixels or page metadata differ: " + name);
                }
            }
        }
    }
}
