// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif;
using Mediana.Core.Video;

namespace Mediana.MediaComparison
{
    internal static class HeifDecodeApplication
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length != 2 && args.Length != 3)
            {
                await Console.Error.WriteLineAsync("Usage: heif-decode input.heic output.rgba [nearest|bilinear|bicubic|lanczos3]");
                return 1;
            }
            string name = args.Length == 3 ? args[2] : "bilinear";
            ChromaUpsamplingFilter filter = name switch
            {
                "nearest" => ChromaUpsamplingFilter.NearestNeighbor,
                "bilinear" => ChromaUpsamplingFilter.Bilinear,
                "bicubic" => ChromaUpsamplingFilter.Bicubic,
                "lanczos3" => ChromaUpsamplingFilter.Lanczos3,
                _ => throw new ArgumentException("Unknown chroma reconstruction filter.", nameof(args))
            };
            await using FileStream input = File.OpenRead(args[0]);
            HeicDocument document = await HeicDocument.LoadAsync(input, new(filter));
            RgbaImage image = await document.DecodePrimaryAsync();
            await using FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write);
            await output.WriteAsync(image.Pixels);
            await output.FlushAsync();
            await Console.Out.WriteLineAsync($"{image.Width}x{image.Height}; source-color RGBA8; chroma {name}; {image.Pixels.Length} bytes.");
            return 0;
        }
    }
}
