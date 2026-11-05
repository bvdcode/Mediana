// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mediana.WebpBenchmarks
{
    internal static class BenchmarkAnimation
    {
        internal static async Task WriteMixedAsync(string directory)
        {
            MediaBufferAllocator allocator = new(long.MaxValue);
            using VideoFrame red = Solid(6, 6, new Rgba32(255, 0, 0), allocator);
            using VideoFrame cyan = Solid(2, 2, new Rgba32(0, 255, 255), allocator);
            using VideoFrame green = Solid(2, 2, new Rgba32(0, 255, 0, 128), allocator);
            WebpAnimationFrame[] frames =
            [
                new(red),
                new(cyan)
                {
                    X = 2, Y = 2, Disposal = WebpFrameDisposal.Background,
                    Encoding = new() { Compression = WebpCompression.Lossy, Quality = 75 }
                },
                new(green) { Blending = WebpFrameBlending.AlphaBlend }
            ];
            await using FileStream output = File.Create(Path.Combine(directory, "mixed-animation.webp"));
            await WebpAnimationEncoder.EncodeAsync(EnumerateRectangles(frames), output, allocator, new(6, 6) { LoopCount = 3 });
        }

        private static VideoFrame Solid(int width, int height, Rgba32 color, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(width * height * 4);
            Span<byte> pixels = writable.Memory.Span;
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                pixels[offset] = color.R; pixels[offset + 1] = color.G; pixels[offset + 2] = color.B; pixels[offset + 3] = color.A;
            }
            using MediaBuffer owned = writable.Publish();
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            return new(format, owned, format.CreateTightLayout(), duration: new(40, new(1, 1000)));
        }

        private static async IAsyncEnumerable<WebpAnimationFrame> EnumerateRectangles(IEnumerable<WebpAnimationFrame> frames)
        {
            await Task.CompletedTask;
            foreach (WebpAnimationFrame frame in frames) { yield return frame; }
        }

        internal static async Task VerifyAsync(VideoFrame input, byte[] pixels, MediaBufferAllocator allocator, string directory)
        {
            List<VideoFrame> frames = [];
            try
            {
                for (int index = 0; index < 3; index++)
                {
                    using WritableMediaBuffer writable = allocator.Allocate(pixels.Length);
                    pixels.CopyTo(writable.Memory);
                    writable.Memory.Span[index * 4] ^= 0xff;
                    using MediaBuffer owned = writable.Publish();
                    frames.Add(new(input.Format, owned, input.Format.CreateTightLayout(), duration: new(40 + index, new(1, 1000))));
                }
                using MemoryStream output = new();
                await WebpAnimationEncoder.EncodeAsync(Enumerate(frames), output, allocator, loopCount: 3);
                await File.WriteAllBytesAsync(Path.Combine(directory, "animation.webp"), output.ToArray());
                using Image<Rgba32> decoded = Image.Load<Rgba32>(output.ToArray());
                if (decoded.Frames.Count != frames.Count || decoded.Metadata.GetWebpMetadata().RepeatCount != 3)
                {
                    throw new InvalidDataException("WebP animation frame count or loop count mismatch.");
                }
                byte[] actual = new byte[pixels.Length];
                for (int index = 0; index < frames.Count; index++)
                {
                    decoded.Frames[index].CopyPixelDataTo(actual);
                    if (!actual.AsSpan().SequenceEqual(frames[index].Data.Span) || decoded.Frames[index].Metadata.GetWebpMetadata().FrameDelay != 40 + index)
                    {
                        throw new InvalidDataException("WebP animation frame pixels or duration mismatch at frame " + index);
                    }
                }
                Console.WriteLine("PASS exact ImageSharp animation RGBA, frame durations and loop count.");
            }
            finally
            {
                foreach (VideoFrame frame in frames) { frame.Dispose(); }
            }
        }

        private static async IAsyncEnumerable<VideoFrame> Enumerate(IEnumerable<VideoFrame> frames)
        {
            await Task.CompletedTask;
            foreach (VideoFrame frame in frames) { yield return frame; }
        }
    }
}
