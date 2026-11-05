// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Core.Metadata
{
    /// <summary>Properties of the primary image, first page, animation canvas or configured model preview.
    /// Dimensions describe the presentation without rasterization; IsPreviewCanvas distinguishes a configured viewport from source dimensions.</summary>
    public class ImageInfo
    {
        public ImageInfo(string formatId, double width, double height, ImageDimensionUnit dimensionUnit,
            int? pageCount = null, MediaDuration? duration = null, double? frameRate = null, bool isPreviewCanvas = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
            if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be finite and positive.");
            }
            if (dimensionUnit is not (ImageDimensionUnit.Pixels or ImageDimensionUnit.Points))
            {
                throw new ArgumentOutOfRangeException(nameof(dimensionUnit));
            }
            if (pageCount is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCount));
            }
            if (frameRate is double rate && (!double.IsFinite(rate) || rate <= 0))
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }
            duration?.TimeBase.Validate();
            IsPreviewCanvas = isPreviewCanvas;
            FormatId = formatId;
            Width = width;
            Height = height;
            DimensionUnit = dimensionUnit;
            PageCount = pageCount;
            Duration = duration;
            FrameRate = frameRate;
        }

        /// <summary>True when dimensions describe a configured model preview, rather than an intrinsic raster or page size.</summary>
        public bool IsPreviewCanvas { get; }
        public string FormatId { get; }
        public double Width { get; }
        public double Height { get; }
        public ImageDimensionUnit DimensionUnit { get; }
        /// <summary>Total page count when the format exposes pages; null otherwise.</summary>
        public int? PageCount { get; }
        public MediaDuration? Duration { get; }
        public double? FrameRate { get; }
    }
}
