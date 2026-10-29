// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.Jpeg2000;

namespace Mediana.Tests
{
    internal static class Jpeg2000ReferenceTestInput
    {
        public static async Task VerifyAsync(string root, string name)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            JsonElement entry = manifest.RootElement.EnumerateArray().Single(value => value.GetProperty("name").GetString() == name);
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, entry.GetProperty("file").GetString()!));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".raw"));
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != entry.GetProperty("sha256").GetString() ||
                Convert.ToHexStringLower(SHA256.HashData(expected)) != entry.GetProperty("rawSha256").GetString())
            {
                throw new InvalidDataException("JPEG 2000 fixture checksum mismatch: " + name);
            }
            Jpeg2000Image image = Jpeg2000Decoder.Decode(input);
            int bits = entry.GetProperty("bits").GetInt32();
            bool signed = entry.GetProperty("signed").GetBoolean();
            if (image.Width != entry.GetProperty("width").GetInt32() || image.Height != entry.GetProperty("height").GetInt32() ||
                image.ComponentCount != entry.GetProperty("components").GetInt32() || image.GetComponent(0).BitDepth != bits ||
                image.GetComponent(0).IsSigned != signed)
            {
                throw new InvalidDataException("JPEG 2000 component metadata mismatch: " + name);
            }
            int maximumError = 0;
            int offset = 0;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    for (int c = 0; c < image.ComponentCount; c++)
                    {
                        int value = bits == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(offset, 2)) : expected[offset];
                        if (signed && value >= 1 << (bits - 1))
                        {
                            value -= 1 << bits;
                        }
                        maximumError = Math.Max(maximumError, Math.Abs(image.GetSample(x, y, c) - value));
                        offset += bits / 8;
                    }
                }
            }
            if (offset != expected.Length || maximumError > entry.GetProperty("maximumError").GetInt32())
            {
                throw new InvalidDataException($"JPEG 2000 samples differ for {name}: maximum error {maximumError}.");
            }
        }

        public static async Task VerifyAllAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                await VerifyAsync(root, entry.GetProperty("name").GetString()!);
            }
        }
    }
}
