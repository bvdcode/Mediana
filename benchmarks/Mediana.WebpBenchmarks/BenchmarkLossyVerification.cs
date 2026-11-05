// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mediana.WebpBenchmarks
{
    internal static class BenchmarkLossyVerification
    {
        internal static async Task WriteAsync(VideoFrame input, byte[] pixels, int quality, MediaBufferAllocator allocator, string directory, bool verify)
        {
            using MediaBuffer encoded = Mediana.Codecs.Webp.WebpEncoder.Encode(input, allocator,
                new WebpEncodingOptions { Compression = WebpCompression.Lossy, Quality = quality });
            await File.WriteAllBytesAsync(Path.Combine(directory, "output.webp"), encoded.Memory.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, "input.rgba"), pixels);
            if (!verify) { return; }
            using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded.Memory.Span);
            if (decoded.Width != input.Format.Width || decoded.Height != input.Format.Height) { throw new InvalidDataException("Lossy WebP dimensions differ."); }
            byte[] actual = new byte[pixels.Length];
            decoded.CopyPixelDataTo(actual);
            long error = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                if (pixels[index + 3] != actual[index + 3]) { throw new InvalidDataException("Lossy WebP alpha differs at pixel " + index / 4); }
                for (int channel = 0; channel < 3; channel++)
                {
                    int difference = pixels[index + channel] - actual[index + channel];
                    error += difference * difference;
                }
            }
            await File.WriteAllBytesAsync(Path.Combine(directory, "decoded.rgba"), actual);
            double mse = (double)error / (pixels.Length / 4 * 3);
            double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255 * 255 / mse);
            Console.WriteLine(FormattableString.Invariant($"PASS ImageSharp VP8 {input.Format.Width}x{input.Format.Height}; quality {quality}; {encoded.Memory.Length} bytes; RGB PSNR {psnr:F2} dB; alpha exact."));
        }
    }
}
