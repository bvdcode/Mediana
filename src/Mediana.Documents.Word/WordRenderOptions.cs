// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Video;

namespace Mediana.Documents.Word
{
    /// <summary>Page rasterization settings. Layout coordinates are points; resolution does not change pagination.</summary>
    public record WordRenderOptions
    {
        public double Dpi { get; init; } = 96;
        public int? MaximumWidth { get; init; }
        public int? MaximumHeight { get; init; }
        public int Supersampling { get; init; } = 2;
        public int MaximumSamples { get; init; } = 16_777_216;
        /// <summary>Source image validation limit. Large PNG pictures are decoded to the page resolution using bounded scanlines.</summary>
        public int MaximumImagePixels { get; init; } = 67_108_864;
        /// <summary>Optional explicit interpretation of embedded image colors. Without it, complete source color metadata is required.</summary>
        public Func<VideoFormat, VideoColorInfo>? ImageColorResolver { get; init; }
        public Rgba32 Background { get; init; } = new(255, 255, 255);
        public Action<string>? WarningHandler { get; init; }
    }
}
