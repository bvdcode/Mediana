// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Video;
using Mediana.Codecs;
using Mediana.Imaging;

namespace Mediana.Documents.Word
{
    /// <summary>Page rasterization settings. Layout coordinates are points; resolution does not change pagination.</summary>
    public record WordRenderOptions : PageRasterOptions
    {
        /// <summary>Source image validation limit. Large PNG pictures are decoded to the page resolution using bounded scanlines.</summary>
        public int MaximumImagePixels { get; init; } = 67_108_864;
        /// <summary>Shared embedded image decoders. Registered document decoding uses the engine's registry.</summary>
        public ImageDecodeContext Images { get; init; } = ImageResources.DefaultDecoders;
        /// <summary>Optional explicit interpretation of embedded image colors. Without it, complete source color metadata is required.</summary>
        public Func<VideoFormat, VideoColorInfo>? ImageColorResolver { get; init; }
        public Rgba32 Background { get; init; } = new(255, 255, 255);
        public Action<string>? WarningHandler { get; init; }
    }
}
