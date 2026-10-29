// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jbig2
{
    public partial class Jbig2Page
    {
        /// <summary>Expands this page's packed black/white samples to an owned opaque sRGB frame.
        /// The allocator counts only the output; decoder and packed page storage are separate.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, SampleAspectRatio sampleAspectRatio,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            VideoFormat format = new(Width, Height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, sampleAspectRatio);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[0].EndOffset));
            ReadOnlySpan<byte> source = Data.Span;
            Span<byte> output = writable.Memory.Span;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    int black = (source[y * Stride + x / 8] >> (7 - x % 8)) & 1;
                    byte value = (byte)(255 * (1 - black));
                    int offset = (y * Width + x) * 4;
                    output[offset] = output[offset + 1] = output[offset + 2] = value;
                    output[offset + 3] = 255;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, layout);
        }
    }
}
