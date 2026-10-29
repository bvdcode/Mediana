// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Security.Cryptography;
using System.Text.Json;
using Mediana.Images.Heif;
using Mediana.Images.Heif.Hevc;

namespace Mediana.Tests
{
    internal static class HeifReferenceTestInput
    {
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
                    IntraSliceHeader slice = await IntraPictureFixture.ReadSliceAsync(name);
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
                HeicDocument document = HeicDocument.Load(input, new HeicDecodeOptions(maxPixels: entry.GetProperty("maxPixels").GetInt32()));
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
