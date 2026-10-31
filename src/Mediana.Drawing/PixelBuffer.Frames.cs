// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Drawing
{
    public partial class PixelBuffer
    {
        /// <summary>Transfers pixels into a frame without copying, keeping their original allocation budget.
        /// Invalidates this owner and all borrowed writable views after publication.</summary>
        public VideoFrame PublishVideoFrame(CancellationToken cancellationToken = default,
            MediaTimestamp? timestamp = null, MediaDuration? duration = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            timestamp?.TimeBase.Validate();
            duration?.TimeBase.Validate();
            VideoFormat format = new(Width, Height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            PlaneLayout[] layout = format.CreateTightLayout();
            using MediaBuffer buffer = _pixels.Publish();
            return new VideoFrame(format, buffer, layout, timestamp, duration);
        }

        /// <summary>Copies the drawing surface's straight sRGB pixels into an independent frame.
        /// Charges the destination to the supplied allocator; the surface keeps its original budget.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, CancellationToken cancellationToken = default,
            MediaTimestamp? timestamp = null, MediaDuration? duration = null)
            => RgbaFrame.CopyFrom(Pixels.Span, Width, Height, VideoColorInfo.Srgb, allocator,
                SampleAspectRatio.Square, cancellationToken, timestamp, duration);
    }
}
