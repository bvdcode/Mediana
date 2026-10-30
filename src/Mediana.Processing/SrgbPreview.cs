// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class SrgbPreview
    {
        /// <summary>Fits supported SDR YUV420P8 or straight RGBA32 to a square-pixel sRGB preview in one pass.
        /// Uses the same nearest-neighbor grid as DisplayResize after SrgbConverter, including SAR and exact
        /// orthogonal rotation/reflection. Converts only selected pixels without a full-size RGB buffer.
        /// Requires known color and sample aspect ratio. Borrows input; returns an independent owned frame.</summary>
        public static VideoFrame Fit(VideoFrame input, VideoDisplayMatrix displayMatrix, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return Fit(input, input.Format.Color, displayMatrix, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        /// <summary>Fits a preview using an explicit source color interpretation without changing frame metadata.</summary>
        public static VideoFrame Fit(VideoFrame input, VideoColorInfo sourceColor, VideoDisplayMatrix displayMatrix,
            int maximumWidth, int maximumHeight, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(sourceColor);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("A preview requires a known sample aspect ratio.");
            OrthogonalTransform orientation = OrthogonalTransform.FromMatrix(displayMatrix);
            NearestNeighborMap map = NearestNeighborMap.Fit(input.Format, aspectRatio, orientation, maximumWidth, maximumHeight);
            return SrgbConverter.Apply(input, sourceColor, map, SampleAspectRatio.Square, allocator, cancellationToken);
        }
    }
}
