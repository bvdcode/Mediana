// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;

namespace Mediana
{
    /// <summary>Selects a document page, image animation or container video through the same module registry.</summary>
    public record VisualOpenOptions
    {
        public ImageDecodeRequest Request { get; init; } = new();
        public ImageOpenOptions Images { get; init; } = new();
        public MediaOpenOptions Media { get; init; } = new();
        public int? StreamIndex { get; init; }
        /// <summary>Enumerate every animation frame instead of one selected image. Video always remains sequential.</summary>
        public bool ReadImageSequence { get; init; }

        internal void Validate()
        {
            ArgumentNullException.ThrowIfNull(Request);
            ArgumentNullException.ThrowIfNull(Images);
            ArgumentNullException.ThrowIfNull(Media);
            Images.Validate(); Media.Validate();
            ArgumentOutOfRangeException.ThrowIfNegative(Request.PageIndex);
            if (StreamIndex is < 0) { throw new ArgumentOutOfRangeException(nameof(StreamIndex)); }
            if (Request.Time is Core.Timing.MediaTimestamp time)
            {
                time.TimeBase.Validate();
                ArgumentOutOfRangeException.ThrowIfNegative(time.Value);
            }
        }
    }
}
