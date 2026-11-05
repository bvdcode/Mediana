// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mediana.WebpBenchmarks
{
    internal static class BenchmarkPixels
    {
        internal static byte[] Create(string kind, ref int width, ref int height)
        {
            if (File.Exists(kind))
            {
                using Image<Rgba32> image = Image.Load<Rgba32>(kind);
                width = image.Width;
                height = image.Height;
                byte[] result = new byte[width * height * 4];
                image.CopyPixelDataTo(result);
                return result;
            }
            byte[] pixels = new byte[width * height * 4];
            uint random = 0x723fa129;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    switch (kind)
                    {
                        case "flat":
                            pixels[offset] = 63; pixels[offset + 1] = 147; pixels[offset + 2] = 219; pixels[offset + 3] = 255;
                            break;
                        case "gradient":
                            pixels[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
                            pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                            pixels[offset + 2] = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
                            pixels[offset + 3] = 255;
                            break;
                        case "alpha":
                            pixels[offset] = (byte)(x * 7); pixels[offset + 1] = (byte)(y * 11);
                            pixels[offset + 2] = (byte)(x ^ y); pixels[offset + 3] = (byte)(x + y);
                            break;
                        case "noise":
                            for (int channel = 0; channel < 3; channel++)
                            {
                                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                                pixels[offset + channel] = (byte)random;
                            }
                            pixels[offset + 3] = 255;
                            break;
                        default: throw new ArgumentException("Unknown pixel sample: " + kind, nameof(kind));
                    }
                }
            }
            return pixels;
        }
    }
}
