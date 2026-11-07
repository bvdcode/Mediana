// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana
{
    /// <summary>Explicit presentation policy. Without a color resolver, conversion requires declared color information.</summary>
    public record VisualExportOptions
    {
        public VisualOpenOptions Input { get; init; } = new();
        public int MaximumWidth { get; init; } = int.MaxValue;
        public int MaximumHeight { get; init; } = int.MaxValue;
        public Func<VideoFormat, VideoColorInfo>? ColorResolver { get; init; }
        public Action<string>? WarningHandler { get; init; }

        internal void Validate()
        {
            ArgumentNullException.ThrowIfNull(Input);
            Input.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumHeight);
        }
    }
}
