// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Core.Metadata
{
    /// <summary>Declared properties of the primary image, first page or animation canvas.
    /// Dimensions describe the presentation after declared orientation and cropping, without rasterization.</summary>
    public class ImageInfo
    {
        public ImageInfo(string formatId, double width, double height, ImageDimensionUnit dimensionUnit,
            int? pageCount = null, MediaDuration? duration = null, double? frameRate = null)
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
            FormatId = formatId;
            Width = width;
            Height = height;
            DimensionUnit = dimensionUnit;
            PageCount = pageCount;
            Duration = duration;
            FrameRate = frameRate;
        }

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
