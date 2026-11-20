// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class VideoFormatComparison
    {
        public static bool GeometryMatches(VideoFormat left, VideoFormat right) =>
            left.Width == right.Width && left.Height == right.Height && left.PixelFormat == right.PixelFormat &&
            left.ScanMode == right.ScanMode && left.FullFrameHeight == right.FullFrameHeight && left.SampleAspectRatio == right.SampleAspectRatio;
    }
}
