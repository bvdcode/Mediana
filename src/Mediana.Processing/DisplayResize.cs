// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class DisplayResize
    {
        /// <summary>Fits RGBA32 into a box with square output pixels using nearest-neighbor sampling.
        /// The unscaled display size is storage width times SAR, by storage height. It is never enlarged.
        /// Dimensions are rounded to the nearest pixel, with a minimum of one. Borrows input; returns an owned frame.</summary>
        public static VideoFrame Fit(VideoFrame input, int maximumWidth, int maximumHeight, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            cancellationToken.ThrowIfCancellationRequested();
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("Display resizing requires a known sample aspect ratio or an explicit interpretation.");
            return Fit(input, aspectRatio, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        /// <summary>Fits using an explicit interpretation of the source sample aspect ratio, without changing input metadata.</summary>
        public static VideoFrame Fit(VideoFrame input, SampleAspectRatio sourceAspectRatio, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            return FitCore(input, sourceAspectRatio, OrthogonalTransform.Identity, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        /// <summary>Fits the transformed image into a tight square-pixel rectangle. Supports exact right-angle rotations
        /// and reflections. Canvas translation is normalized to the output origin; no canvas padding is added.</summary>
        public static VideoFrame Fit(VideoFrame input, VideoDisplayMatrix sourceDisplayMatrix, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            cancellationToken.ThrowIfCancellationRequested();
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("Display resizing requires a known sample aspect ratio or an explicit interpretation.");
            return Fit(input, aspectRatio, sourceDisplayMatrix, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        /// <summary>Fits an oriented image using explicit interpretations of source SAR and display matrix.</summary>
        public static VideoFrame Fit(VideoFrame input, SampleAspectRatio sourceAspectRatio, VideoDisplayMatrix sourceDisplayMatrix,
            int maximumWidth, int maximumHeight, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            OrthogonalTransform orientation = OrthogonalTransform.FromMatrix(sourceDisplayMatrix);
            return FitCore(input, sourceAspectRatio, orientation, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        private static VideoFrame FitCore(VideoFrame input, SampleAspectRatio sourceAspectRatio, OrthogonalTransform orientation,
            int maximumWidth, int maximumHeight, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            NearestNeighborMap map = NearestNeighborMap.Fit(input.Format, sourceAspectRatio, orientation, maximumWidth, maximumHeight);
            return NearestNeighborResize.Apply(input, map, SampleAspectRatio.Square, allocator, cancellationToken);
        }
    }
}
