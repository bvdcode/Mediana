// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Images.Tgs;

namespace Mediana.OperationComparison
{
    internal static class AnimationComparison
    {
        internal static async Task DecodeWebpAsync(string input, string destination)
        {
            byte[] encoded = await File.ReadAllBytesAsync(input);
            MediaBufferAllocator allocator = new(256 * 1024 * 1024);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            WebpImageDecoder decoder = new();
            int frames = 0;
            await foreach (VideoFrame frame in decoder.DecodeFramesAsync(encoded, new(), allocator))
            {
                using (frame)
                {
                    if (frame.Format.PixelFormat != VideoPixelFormat.Rgba32)
                    {
                        throw new InvalidDataException("Expected straight RGBA8 animation frames.");
                    }
                    PlaneLayout plane = frame.Planes[0];
                    for (int row = 0; row < plane.Height; row++)
                    {
                        await output.WriteAsync(frame.Data.Slice(plane.Offset + row * plane.Stride, plane.RowBytes));
                    }
                    frames++;
                }
            }
            if (frames == 0 || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Empty animation or retained frame storage.");
            }
            await Console.Out.WriteLineAsync($"frames={frames}; retained_bytes=0");
        }

        internal static async Task RenderTgsAsync(string input, string destination)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(input);
            DrawingRenderOptions options = new() { Width = document.Width, Height = document.Height, Supersampling = 1 };
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
            int frames = 0;
            foreach (TgsFrame frame in TgsRenderer.RenderFrames(document, null, options))
            {
                using PixelBuffer image = frame.Image;
                if (frame.Warnings.Count != 0) { throw new InvalidDataException("Animation produced rendering warnings."); }
                await output.WriteAsync(image.Pixels);
                frames++;
            }
            if (frames == 0) { throw new InvalidDataException("Empty animation."); }
            await Console.Out.WriteLineAsync($"frames={frames}");
        }
    }
}
