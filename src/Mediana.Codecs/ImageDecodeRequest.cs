// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs
{
    /// <summary>Selects a zero-based page or an elapsed animation time. Zero time selects the opening image.</summary>
    public record ImageDecodeRequest
    {
        public int PageIndex { get; init; }
        public MediaTimestamp? Time { get; init; }

        public void Validate(ImageDecoderFeatures features)
        {
            if ((features & ~(ImageDecoderFeatures.Pages | ImageDecoderFeatures.Animation)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(features));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(PageIndex);
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
