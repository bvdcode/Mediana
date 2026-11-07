// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    public record VideoPreparationOptions
    {
        public int? MaximumWidth { get; init; }
        public int? MaximumHeight { get; init; }
        /// <summary>Null preserves decoded precision. Selecting YUV420P8 explicitly permits rounded ten-to-eight-bit
        /// sample conversion; it does not permit changing colorimetry or tone mapping.</summary>
        public VideoPixelFormat? OutputPixelFormat { get; init; }
        /// <summary>Explicitly permits opaque RGBA8 conversion to YUV420P8 before orientation and scaling.</summary>
        public RgbVideoConversionOptions? RgbConversion { get; init; }
    }
}
