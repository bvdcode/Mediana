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

        public void Validate(ImageDecoderFeatures features)
        {
            if ((features & ~(ImageDecoderFeatures.Pages | ImageDecoderFeatures.Animation)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(PageIndex);
            if (MaximumPixels is <= 0 || PreferredWidth is <= 0 || PreferredHeight is <= 0) { throw new ArgumentOutOfRangeException(nameof(MaximumPixels)); }
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
