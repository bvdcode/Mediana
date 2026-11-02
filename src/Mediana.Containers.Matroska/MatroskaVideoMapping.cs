// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Containers.Matroska
{
    internal static class MatroskaVideoMapping
    {
        public static bool TryGetAspectRatio(MatroskaVideoSettings video, out VideoAspectRatioDescription? aspectRatio)
        {
            aspectRatio = null;
            if (video.PixelWidth == 0 || video.PixelHeight == 0 || video.CropLeft != 0 || video.CropRight != 0 ||
                video.CropTop != 0 || video.CropBottom != 0 || video.Interlaced == 1 ||
                video.Projection is not null || video.DisplayUnit > 3 || video.StereoMode != 0 || video.AlphaMode != 0)
            {
                return false;
            }
            if (video.Colour is MatroskaColourSettings colour &&
                ((colour.BitsPerChannel is not (0 or 8)) ||
                (colour.ChromaSubsamplingHorz is not (null or 1)) || (colour.ChromaSubsamplingVert is not (null or 1)) ||
                (colour.CbSubsamplingHorz is not (null or 0)) || (colour.CbSubsamplingVert is not (null or 0))))
            {
                return false;
            }
            ulong? width = video.DisplayWidth;
            ulong? height = video.DisplayHeight;
            if (video.DisplayUnit == 0)
            {
                width ??= video.PixelWidth;
                height ??= video.PixelHeight;
            }
            if (width is not ulong displayWidth || height is not ulong displayHeight || displayWidth == 0 || displayHeight == 0)
            {
                return false;
            }
            UInt128 numerator = (UInt128)displayWidth * video.PixelHeight;
            UInt128 denominator = (UInt128)displayHeight * video.PixelWidth;
            UInt128 a = numerator;
            UInt128 b = denominator;
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            numerator /= a;
            denominator /= a;
            if (numerator > int.MaxValue || denominator > int.MaxValue)
            {
                return false;
            }
            aspectRatio = new(new((int)numerator, (int)denominator));
            return true;
        }
    }
}
