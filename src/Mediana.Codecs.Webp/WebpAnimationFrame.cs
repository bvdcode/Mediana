// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    /// <summary>A borrowed frame rectangle. Coordinates must be even; duration comes from Image.Duration in exact milliseconds.
    /// Optional per-frame encoding overrides compression and quality; metadata belongs to the animation options.</summary>
    public record WebpAnimationFrame(VideoFrame Image)
    {
        public int X { get; init; }
        public int Y { get; init; }
        public WebpFrameBlending Blending { get; init; } = WebpFrameBlending.Replace;
        public WebpFrameDisposal Disposal { get; init; } = WebpFrameDisposal.Keep;
        public WebpEncodingOptions? Encoding { get; init; }
    }
}
