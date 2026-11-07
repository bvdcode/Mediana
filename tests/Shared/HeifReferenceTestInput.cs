// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Images.Heif;
using Mediana.Images.Heif.Hevc;
using Mediana.Codecs.Hevc;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class HeifReferenceTestInput
    {
        public static async Task VerifyFiltersAsync(string root)
        {
            if (HeicDecodeOptions.Default.ChromaFilter != ChromaUpsamplingFilter.Bilinear)
            {
                throw new InvalidDataException("Default HEIC color reconstruction must be bilinear.");
            }
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, entry.GetProperty("file").GetString()!));
                RgbaImage nearest = await HeicDocument.Load(input, new(ChromaUpsamplingFilter.NearestNeighbor)).DecodePrimaryAsync();
                foreach (ChromaUpsamplingFilter filter in new[] { ChromaUpsamplingFilter.Bilinear, ChromaUpsamplingFilter.Bicubic,
                    ChromaUpsamplingFilter.Lanczos3 })
                {
                    RgbaImage image = await HeicDocument.Load(input, new(filter)).DecodePrimaryAsync();
                    if (image.Width != nearest.Width || image.Height != nearest.Height)
                    {
                        throw new InvalidDataException("HEIC chroma reconstruction changed output geometry.");
                    }
                    for (int offset = 3; offset < image.Pixels.Length; offset += 4)
                    {
                        if (image.Pixels.Span[offset] != nearest.Pixels.Span[offset])
                        {
                            throw new InvalidDataException("HEIC chroma reconstruction changed alpha.");
                        }
                    }
                }
            }
        }

        public static async Task VerifyChromaAsync(string root)
        {
            using JsonDocument references = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "chroma-reconstruction.json")));
            int width = references.RootElement.GetProperty("width").GetInt32();
            int height = references.RootElement.GetProperty("height").GetInt32();
            foreach (JsonElement entry in references.RootElement.GetProperty("cases").EnumerateArray())
            {
                int maximum = entry.GetProperty("maximum").GetInt32();
                ushort[] samples = entry.GetProperty("source").EnumerateArray().Select(value => value.GetUInt16()).ToArray();
                foreach (ChromaUpsamplingFilter filter in new[] { ChromaUpsamplingFilter.NearestNeighbor,
                    ChromaUpsamplingFilter.Bilinear, ChromaUpsamplingFilter.Bicubic, ChromaUpsamplingFilter.Lanczos3 })
                {
                    ChromaUpsampler plan = new(width, height, 2, 2, ChromaLocation.Center, filter);
                    ushort[] expected = entry.GetProperty("expected").GetProperty(filter.ToString()).EnumerateArray()
                        .Select(value => value.GetUInt16()).ToArray();
                    ushort[] output = new ushort[plan.OutputWidth];
                    float[] workspace = new float[plan.WorkspaceLength];
                    for (int y = 0; y < plan.OutputHeight; y++)
                    {
                        plan.CopyRow<ushort>(samples, width, y, output, workspace, maximum);
                        // Pillow renormalizes truncated kernels; plane reconstruction extends edge samples.
                        int border = filter switch
                        {
                            ChromaUpsamplingFilter.NearestNeighbor or ChromaUpsamplingFilter.Bilinear => 0,
                            ChromaUpsamplingFilter.Bicubic => 4,
                            ChromaUpsamplingFilter.Lanczos3 => 6,
                            _ => throw new ArgumentOutOfRangeException(nameof(filter))
                        };
                        if (y < border || y >= plan.OutputHeight - border)
                        {
                            continue;
                        }
                        for (int x = border; x < output.Length - border; x++)
                        {
                            if (Math.Abs(output[x] - expected[y * output.Length + x]) > 1)
                            {
                                throw new InvalidDataException($"Chroma {filter} differs from the independent reference at {x},{y}.");
                            }
                        }
                    }
                }
            }
        }

        public static async Task VerifyWavefrontAsync()
        {
            (string Name, string Hash)[] cases =
            [
                ("wpp-8bit.hevc", "5EBACC5688E590C1F36DCF166CE7C1EC3449B7F633BB2F39814C9549A6C36256"),
                ("wpp-10bit.hevc", "E3E54B71B1EFA1F0B5AC235D5A0415BC9C88AB6385C7BBB654F7697E7442AF90"),
                ("wpp-mono.hevc", "3CDA530E7B6F60A0831EC43C2FF747B3AE92605FCA5BF6EDD029EC0471B036B9")
            ];
            foreach ((string name, string hash) in cases)
            {
                foreach (int workers in new[] { 1, 4 })
                {
                    HevcSliceHeader slice = await IntraPictureFixture.ReadSliceAsync(name);
                    using DecodedPicture picture = await new IntraPictureDecoder(slice).DecodeAsync(workers, CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(10));
                    if (IntraPictureFixture.PlaneHash(picture) != hash)
                    {
                        throw new InvalidDataException("Wavefront plane mismatch: " + name);
                    }
                }
            }
        }

        public static async Task VerifyAllAsync(string root)
        {
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "manifest.json")));
            foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
            {
                string name = entry.GetProperty("file").GetString()!;
                byte[] input = await File.ReadAllBytesAsync(Path.Combine(root, name));
                if (Convert.ToHexString(SHA256.HashData(input)) != entry.GetProperty("inputSha256").GetString())
                {
                    throw new InvalidDataException("HEIC fixture checksum mismatch: " + name);
                }
                HeicDocument document = HeicDocument.Load(input, new HeicDecodeOptions(maxPixels: entry.GetProperty("maxPixels").GetInt32(),
                    chromaFilter: ChromaUpsamplingFilter.NearestNeighbor));
                RgbaImage image = await document.DecodePrimaryAsync();
                if (image.Width != entry.GetProperty("width").GetInt32() || image.Height != entry.GetProperty("height").GetInt32() ||
                    Convert.ToHexString(SHA256.HashData(image.Pixels.Span)) != entry.GetProperty("rgbaSha256").GetString())
                {
                    throw new InvalidDataException("HEIC pixels or visible dimensions differ: " + name);
                }
            }
        }
    }
}
