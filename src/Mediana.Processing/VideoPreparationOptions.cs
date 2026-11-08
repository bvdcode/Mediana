// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    public record VideoPreparationOptions
    {
        public int? MaximumWidth { get; init; }
        public int? MaximumHeight { get; init; }
        /// <summary>Explicitly replaces source color labels and their source-description interpretation without
        /// converting samples. Null uses the configured policy or preserves the decoded interpretation. Callers must establish the replacement
        /// from knowledge of the source; this option does not infer missing characteristics.</summary>
        public VideoColorInfo? SourceColorInterpretation { get; init; }
        /// <summary>Explicit color policy applied when no source interpretation is supplied. Null preserves strict decoded labels.</summary>
        public Func<VideoFormat, VideoColorInfo>? ColorResolver { get; init; }
        /// <summary>Receives color-interpretation and sample-precision warnings once when a source format is configured.</summary>
        public Action<string>? WarningHandler { get; init; }
        /// <summary>Null preserves decoded precision. Selecting a lower sample precision explicitly permits rounded
        /// sample conversion; it does not permit changing colorimetry or tone mapping.</summary>
        public VideoPixelFormat? OutputPixelFormat { get; init; }
        /// <summary>Explicitly permits opaque RGBA8 or planar GBR conversion to YUV420P8 before orientation and scaling.</summary>
        public RgbVideoConversionOptions? RgbConversion { get; init; }
    }
}
