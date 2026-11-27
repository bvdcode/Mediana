// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Selects a zero-based page or an elapsed animation time. Zero time selects the opening image.</summary>
    public record ImageDecodeRequest
    {
        public int PageIndex { get; init; }
        public MediaTimestamp? Time { get; init; }
        /// <summary>Receives nonfatal diagnostics synchronously during image decoding.</summary>
        public Action<string>? WarningHandler { get; init; }
        /// <summary>Shared decoder scope for embedded resources. Engines supply it automatically.</summary>
        public ImageDecodeContext? Images { get; init; }
        /// <summary>Shared operation resources. Nested decoders inherit this set; an explicit request overrides engine resources.</summary>
        public MediaResourceSet? Resources { get; init; }
        /// <summary>Optional source pixel limit. Decoders validate source dimensions before allocation when supported.</summary>
        public int? MaximumPixels { get; init; }
        /// <summary>Optional decoding resolution hints. Decoders may retain a higher resolution; presentation controls final sizing.</summary>
        public int? PreferredWidth { get; init; }
        public int? PreferredHeight { get; init; }
        /// <summary>Minimum desired sample count on the longer display axis when decoding a preview.
        /// Can be combined with axis-specific hints. Final resizing remains the caller's choice.</summary>
        public int? PreferredLongEdge { get; init; }

        /// <summary>Largest integer reduction that meets all requested dimensions, or one for full resolution.</summary>
        public int GetMaximumReduction(int displayWidth, int displayHeight)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayHeight);
            ValidateResolutionHints();
            if (PreferredWidth is null && PreferredHeight is null && PreferredLongEdge is null) { return 1; }
            int factor = PreferredWidth is int width ? displayWidth / width : int.MaxValue;
            if (PreferredHeight is int height) { factor = Math.Min(factor, displayHeight / height); }
            if (PreferredLongEdge is int edge) { factor = Math.Min(factor, Math.Max(displayWidth, displayHeight) / edge); }
            return Math.Max(1, factor);
        }

        private void ValidateResolutionHints()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PreferredWidth.GetValueOrDefault(1), nameof(PreferredWidth));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PreferredHeight.GetValueOrDefault(1), nameof(PreferredHeight));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PreferredLongEdge.GetValueOrDefault(1), nameof(PreferredLongEdge));
        }

        public void Validate(ImageDecoderFeatures features)
        {
            if ((features & ~(ImageDecoderFeatures.Pages | ImageDecoderFeatures.Animation)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(PageIndex);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPixels.GetValueOrDefault(1), nameof(MaximumPixels));
            ValidateResolutionHints();
            if (Time is MediaTimestamp time)
            {
                time.TimeBase.Validate();
                ArgumentOutOfRangeException.ThrowIfNegative(time.Value);
                if (time.Value != 0 && !features.HasFlag(ImageDecoderFeatures.Animation))
                {
                    throw new NotSupportedException("The image decoder does not support animation time selection.");
                }
            }
            if (PageIndex != 0 && !features.HasFlag(ImageDecoderFeatures.Pages))
            {
                throw new NotSupportedException("The image decoder does not support page selection.");
            }
        }
    }
}
