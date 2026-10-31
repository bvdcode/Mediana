// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264RateEncodingTestInput
    {
        public static async Task VerifyAsync(string root, int mode, int qp, int bitRate, int bufferMilliseconds)
        {
            string name = $"rate-{mode}-qp{qp}-{bitRate}";
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, name + ".yuv"));
            (byte[] transport, byte[] _, byte[] decoded) = await H264TemporalEncodingTestInput.EncodeAsync(mode, qp,
                new() { BitRate = bitRate, BufferDuration = TimeSpan.FromMilliseconds(bufferMilliseconds) });
            if (!expected.AsSpan().SequenceEqual(decoded))
            {
                throw new InvalidDataException("Rate-controlled reconstruction differs from the complete independent reference.");
            }
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "RATE-ENCODING.json")));
            JsonElement entry = manifest.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(transport)), entry.GetProperty("transport_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Rate-controlled transport differs from its complete reference hash.");
            }
        }
    }
}
