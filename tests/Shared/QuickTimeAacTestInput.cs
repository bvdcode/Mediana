// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;

namespace Mediana.Tests
{
    internal static class QuickTimeAacTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "quicktime-aac.reference.json")));
            JsonElement record = reference.RootElement;
            string path = Path.Combine(root, record.GetProperty("file").GetString()!);
            byte[] input = await File.ReadAllBytesAsync(path);
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, record.GetProperty("pcm_file").GetString()!));
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != record.GetProperty("sha256").GetString() ||
                Convert.ToHexStringLower(SHA256.HashData(expected)) != record.GetProperty("pcm_sha256").GetString())
            {
                throw new InvalidDataException("The QuickTime AAC input or independent PCM reference changed.");
            }
            int rate = record.GetProperty("sample_rate").GetInt32();
            int channels = record.GetProperty("channels").GetInt32();
            foreach (JsonElement stream in record.GetProperty("streams").EnumerateArray())
            {
                int index = stream.GetProperty("index").GetInt32();
                (int Sequences, int Shapes, int Books, int Filters, int MidSideBands) features = await AacFeatureTestInput.InspectAsync(path, index);
                if ((features.Books & (1 << 13)) == 0)
                {
                    throw new InvalidDataException("The QuickTime AAC reference no longer exercises noise substitution.");
                }
                await AacDecodeTestInput.VerifyAsync(input, expected, stream.GetProperty("packets"), rate, channels, index, noise: true);
            }
        }
    }
}
