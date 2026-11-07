// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Core.Video
{
    /// <summary>Imports straight, top-down RGBA8 pixels into an independently owned frame.</summary>
    public static class RgbaFrame
    {
        /// <summary>Copies tight rows. The allocator counts the destination, not source or decoder workspace.</summary>
        public static VideoFrame CopyFrom(ReadOnlySpan<byte> pixels, int width, int height,
            VideoColorInfo color, MediaBufferAllocator allocator, SampleAspectRatio? sampleAspectRatio = null,
            CancellationToken cancellationToken = default, MediaTimestamp? timestamp = null, MediaDuration? duration = null)
        {
            ArgumentNullException.ThrowIfNull(color);
            return CopyFrom(pixels, new VideoFormat(width, height, VideoPixelFormat.Rgba32, color, sampleAspectRatio),
                allocator, cancellationToken, timestamp, duration);
        }

        /// <summary>Copies tight RGBA rows, retaining the format's color description and presentation metadata.</summary>
        public static VideoFrame CopyFrom(ReadOnlySpan<byte> pixels, VideoFormat format, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default, MediaTimestamp? timestamp = null, MediaDuration? duration = null)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (format.PixelFormat != VideoPixelFormat.Rgba32)
            {
                throw new ArgumentException("RGBA import requires an RGBA32 format.", nameof(format));
            }
            PlaneLayout[] layout = format.CreateTightLayout();
            int length = checked((int)layout[0].EndOffset);
            if (pixels.Length != length)
            {
                throw new ArgumentException("RGBA byte count does not match the image dimensions.", nameof(pixels));
            }
            using WritableMediaBuffer writable = allocator.Allocate(length);
            int offset = 0;
            while (offset < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(length - offset, 64 * 1024);
                pixels.Slice(offset, count).CopyTo(writable.Memory.Span.Slice(offset, count));
                offset += count;
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, layout, timestamp, duration);
        }
    }
}
