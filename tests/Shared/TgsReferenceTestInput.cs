// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using Mediana.Drawing;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;

namespace Mediana.Tests
{
    internal static class TgsReferenceTestInput
    {
        public static bool ContainsExpressions(string name) => name is "bounce-path-expression.tgs" or
            "controller-expressions.tgs" or "elastic-expressions.tgs" or "missing-color-controller.tgs";

        public static async Task VerifyAllAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "render-reference.json")));
            int compared = 0;
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                if (ContainsExpressions(name))
                {
                    continue;
                }
                TgsDocument document = await TgsReader.ReadFileAsync(Path.Combine(root, name));
                int size = entry.GetProperty("size").GetInt32();
                PixelBuffer image = TgsRenderer.RenderFrame(document, entry.GetProperty("frame").GetDouble(),
                    new SvgRenderOptions { Width = size, Height = size, Supersampling = 1 });
                byte[] reference = new byte[checked(size * size * 4)];
                await using FileStream encoded = File.OpenRead(Path.Combine(root, entry.GetProperty("pixels").GetString()!));
                await using GZipStream pixels = new(encoded, CompressionMode.Decompress);
                await pixels.ReadExactlyAsync(reference);
                byte[] trailing = new byte[1];
                if (await pixels.ReadAsync(trailing) != 0 ||
                    Convert.ToHexStringLower(SHA256.HashData(reference)) != entry.GetProperty("sha256").GetString())
                {
                    throw new InvalidDataException("TGS reference pixels are corrupt: " + name);
                }
                VerifyPixels(image.Pixels.Span, reference, name);
                compared++;
            }
            if (compared != 33)
            {
                throw new InvalidDataException("TGS reference corpus must compare 33 supported frames.");
            }
        }

        internal static void VerifyPixels(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> reference, string name)
        {
            if (actual.Length != reference.Length || actual.Length % 4 != 0)
            {
                throw new InvalidDataException("TGS pixel dimensions differ: " + name);
            }
            for (int i = 0; i < actual.Length; i++)
            {
                int tolerance = i % 4 == 3 ? 0 : 1;
                if (Math.Abs(actual[i] - reference[i]) > tolerance)
                {
                    throw new InvalidDataException($"TGS reference pixels differ: {name}, byte {i}.");
                }
            }
        }
    }
}
