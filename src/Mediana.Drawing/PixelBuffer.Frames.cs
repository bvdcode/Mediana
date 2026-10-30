// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Drawing
{
    public partial class PixelBuffer
    {
        /// <summary>Copies the drawing surface's straight sRGB pixels into an independent frame.
        /// The allocator counts the copy, not the drawing surface or rendering workspace.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, CancellationToken cancellationToken = default,
            MediaTimestamp? timestamp = null, MediaDuration? duration = null)
            => RgbaFrame.CopyFrom(Pixels.Span, Width, Height, VideoColorInfo.Srgb, allocator,
                SampleAspectRatio.Square, cancellationToken, timestamp, duration);
    }
}
