// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Imaging;
namespace Mediana.Documents.Presentation
{
    public record PresentationRenderOptions : PageRasterOptions
    {
        public int MaximumImagePixels { get; init; } = 67_108_864;
        public TextOptions Text { get; init; } = new();
        public ImageDecodeContext Images { get; init; } = ImageResources.DefaultDecoders;
        public Func<VideoFormat, VideoColorInfo>? ImageColorResolver { get; init; }
        public Action<string>? WarningHandler { get; init; }
    }
}
