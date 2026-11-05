// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mediana.WebpBenchmarks
{
    internal static class BenchmarkAnimationInspection
    {
        internal static async Task ReadAsync(string input, string output)
        {
            using Image<Rgba32> image = await Image.LoadAsync<Rgba32>(input);
            await using FileStream pixels = File.Create(Path.Combine(output, "animation.rgba"));
            byte[] framePixels = new byte[checked(image.Width * image.Height * 4)];
            long duration = 0;
            foreach (ImageFrame<Rgba32> frame in image.Frames)
            {
                frame.CopyPixelDataTo(framePixels);
                await pixels.WriteAsync(framePixels);
                duration += frame.Metadata.GetWebpMetadata().FrameDelay;
            }
            Console.WriteLine(FormattableString.Invariant($"PASS independent animation: {image.Width}x{image.Height}; {image.Frames.Count} frames; {duration} ms; loops {image.Metadata.GetWebpMetadata().RepeatCount}."));
        }
    }
}
