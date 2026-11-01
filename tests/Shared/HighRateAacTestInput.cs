// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;

namespace Mediana.Tests
{
    internal static class HighRateAacTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "automatic-96k.json")));
            JsonElement reference = document.RootElement;
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, reference.GetProperty("file").GetString()!));
            byte[] pcm = await File.ReadAllBytesAsync(Path.Combine(root, reference.GetProperty("pcm_file").GetString()!));
            if (Convert.ToHexStringLower(SHA256.HashData(input)) != reference.GetProperty("input_sha256").GetString() ||
                Convert.ToHexStringLower(SHA256.HashData(pcm)) != reference.GetProperty("pcm_sha256").GetString())
            {
                throw new InvalidDataException("The complete high-rate AAC input or independent PCM reference changed.");
            }
            await AacDecodeTestInput.VerifyAsync(input, pcm, reference.GetProperty("packets"),
                reference.GetProperty("sample_rate").GetInt32(), reference.GetProperty("channels").GetInt32(), 0, false);
        }
    }
}
