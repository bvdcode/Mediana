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
