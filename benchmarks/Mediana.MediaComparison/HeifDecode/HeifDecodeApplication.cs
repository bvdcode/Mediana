// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif;

namespace Mediana.MediaComparison
{
    internal static class HeifDecodeApplication
    {
        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length != 2)
            {
                await Console.Error.WriteLineAsync("Usage: heif-decode input.heic output.rgba");
                return 1;
            }
            await using FileStream input = File.OpenRead(args[0]);
            HeicDocument document = await HeicDocument.LoadAsync(input);
            RgbaImage image = await document.DecodePrimaryAsync();
            await using FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write);
            await output.WriteAsync(image.Pixels);
            await output.FlushAsync();
            await Console.Out.WriteLineAsync($"{image.Width}x{image.Height}; source-color RGBA8; {image.Pixels.Length} bytes.");
            return 0;
        }
    }
}
