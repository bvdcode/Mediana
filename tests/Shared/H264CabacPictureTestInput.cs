// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264CabacPictureTestInput
    {
        public static async Task VerifyAsync(string root, int mode, int qp, H264RateControlOptions? rate = null,
            H264MacroblockAnalysis analysis = H264MacroblockAnalysis.Fast)
        {
            string prefix = analysis switch
            {
                H264MacroblockAnalysis.Fast => "cabac",
                H264MacroblockAnalysis.RateDistortion => "cabac-rd",
                _ => throw new ArgumentOutOfRangeException(nameof(analysis))
            };
            string name = $"{prefix}-{mode}-qp{qp}";
            if (rate is not null) { name += $"-{rate.BitRate}"; }
            (byte[] transport, byte[] _, byte[] actual) = await H264TemporalEncodingTestInput.EncodeAsync(mode, qp,
                rate, H264EntropyCoding.Cabac, analysis);
            await using FileStream input = File.OpenRead(Path.Combine(root, name + ".yuv.gz"));
            await using GZipStream compressed = new(input, CompressionMode.Decompress);
            using MemoryStream reference = new();
            await compressed.CopyToAsync(reference);
            if (!actual.AsSpan().SequenceEqual(reference.GetBuffer().AsSpan(0, (int)reference.Length)))
            {
                throw new InvalidDataException("CABAC reconstruction differs from complete independent reference pixels.");
            }
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "references.json")));
            JsonElement entry = manifest.RootElement.EnumerateArray().Single(item => item.GetProperty("case").GetString() == name);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(transport)),
                entry.GetProperty("transport_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("CABAC transport differs from its independently decoded reference.");
            }
        }
    }
}
