// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static class SrgbPreview
    {
        /// <summary>Fits SDR planar integer YUV/GBR or straight RGBA32 to a square-pixel sRGB preview in one pass.
        /// Uses the same nearest-neighbor grid as DisplayResize after SrgbConverter, including SAR and exact
        /// orthogonal rotation/reflection. Converts only selected pixels without a full-size RGB buffer.
        /// Requires a supported ICC profile or known color labels, and a known sample aspect ratio. Borrows input; returns an independent owned frame.</summary>
        public static VideoFrame Fit(VideoFrame input, VideoDisplayMatrix displayMatrix, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            return FitWithColorPolicy(input, null, displayMatrix, maximumWidth, maximumHeight, allocator, cancellationToken);
        }

        /// <summary>Fits a preview using an embedded ICC transform, or the supplied policy for color labels.</summary>
        public static VideoFrame FitWithColorPolicy(VideoFrame input, Func<VideoFormat, VideoColorInfo>? colorResolver,
            VideoDisplayMatrix displayMatrix, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            return FitCore(input, colorResolver, displayMatrix, maximumWidth, maximumHeight, allocator, cancellationToken, null);
        }

        /// <summary>Fits a preview with explicit PQ/HLG tone mapping, converting only the selected pixels.</summary>
        public static VideoFrame FitWithColorPolicy(VideoFrame input, Func<VideoFormat, VideoColorInfo>? colorResolver,
            HdrToneMappingOptions toneMapping, VideoDisplayMatrix displayMatrix, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(toneMapping);
            toneMapping.Validate();
            return FitCore(input, colorResolver, displayMatrix, maximumWidth, maximumHeight, allocator, cancellationToken, toneMapping);
        }

        private static VideoFrame FitCore(VideoFrame input, Func<VideoFormat, VideoColorInfo>? colorResolver,
            VideoDisplayMatrix displayMatrix, int maximumWidth, int maximumHeight, MediaBufferAllocator allocator,
            CancellationToken cancellationToken, HdrToneMappingOptions? toneMapping)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            SampleAspectRatio aspectRatio = input.Format.SampleAspectRatio
                ?? throw new NotSupportedException("A preview requires a known sample aspect ratio.");
            OrthogonalTransform orientation = OrthogonalTransform.FromMatrix(displayMatrix);
            NearestNeighborMap map = NearestNeighborMap.Fit(input.Format, aspectRatio, orientation, maximumWidth, maximumHeight);
            return SrgbConverter.ApplyWithColorPolicy(input, colorResolver, map, SampleAspectRatio.Square, allocator, cancellationToken, toneMapping);
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
