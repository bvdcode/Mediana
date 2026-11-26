// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Ffv1
{
    internal static class Ffv1VideoFormat
    {
        internal static VideoFormat Create(EncodedStreamInfo stream, Ffv1StreamConfiguration configuration, VideoPixelFormat format,
            SampleAspectRatio? aspect = null, VideoScanMode? scan = null) =>
            new(configuration.Width, configuration.Height, format,
                new(ChromaLocation: ChromaLocation.Left), stream.AspectRatioDescription?.Resolve(aspect) ?? aspect ?? SampleAspectRatio.Square,
                stream.ColorDescription, scan ?? VideoScanMode.Progressive);
    }
}
