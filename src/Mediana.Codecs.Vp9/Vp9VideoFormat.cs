// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core;
using Mediana.Core.Video;

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9VideoFormat
    {
        internal static VideoFormat Create(Vp9PictureGeometry geometry, EncodedStreamInfo stream)
        {
            VideoPixelFormat pixels;
            if (geometry.SubsamplingX && geometry.SubsamplingY)
            {
                pixels = geometry.BitDepth switch
                { 8 => VideoPixelFormat.Yuv420P8, 10 => VideoPixelFormat.Yuv420P10, 12 => VideoPixelFormat.Yuv420P12, _ => throw new InvalidDataException("Invalid VP9 sample precision.") };
            }
            else
            {
                string sampling = (geometry.SubsamplingX, geometry.SubsamplingY) switch
                { (false, false) => "444", (true, false) => "422", (false, true) => "440", (true, true) => throw new InvalidOperationException("Unexpected subsampled VP9 format.") };
                pixels = geometry.ColorSpace == 7 ? new PlanarVideoPixelFormat($"gbrp{geometry.BitDepth}", PlanarColorModel.Gbr, geometry.BitDepth)
                    : new PlanarVideoPixelFormat($"yuv{sampling}p{geometry.BitDepth}", PlanarColorModel.Yuv, geometry.BitDepth, geometry.SubsamplingX ? 2 : 1, geometry.SubsamplingY ? 2 : 1);
            }
            VideoColorInfo color = geometry.ColorSpace switch
            {
                0 => new(),
                1 or 3 => new(ColorPrimaries.Smpte170M, TransferFunction.Bt709, MatrixCoefficients.Bt601),
                2 => new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709),
                4 => new(Transfer: TransferFunction.Smpte240M, Matrix: MatrixCoefficients.Smpte240M),
                5 => new(ColorPrimaries.Bt2020, TransferFunction.Bt709, MatrixCoefficients.Bt2020NonConstant),
                7 => VideoColorInfo.Srgb,
                _ => throw new InvalidDataException("Invalid VP9 color space.")
            };
            color = color with { Range = geometry.FullRange ? ColorRange.Full : ColorRange.Limited,
                ChromaLocation = geometry.ColorSpace == 7 ? ChromaLocation.Unspecified : ChromaLocation.Center };
            long numerator = (long)geometry.RenderWidth * geometry.Height, denominator = (long)geometry.RenderHeight * geometry.Width;
            long divisor = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
            SampleAspectRatio aspect = new(checked((int)(numerator / divisor)), checked((int)(denominator / divisor)));
            return new(geometry.Width, geometry.Height, pixels, color, stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        internal static PlaneLayout[] Layout(Vp9Picture picture)
        {
            int bytes = picture.Geometry.BitDepth == 8 ? 1 : 2, sx = picture.Geometry.SubsamplingX ? 1 : 0, sy = picture.Geometry.SubsamplingY ? 1 : 0;
            int yLength = picture.Stride * picture.PaddedHeight * bytes, uvLength = yLength >> (sx + sy);
            return [new(0, picture.Stride * bytes, picture.Geometry.Width * bytes, picture.Geometry.Height),
                new(yLength, (picture.Stride >> sx) * bytes, ((picture.Geometry.Width + sx) >> sx) * bytes, (picture.Geometry.Height + sy) >> sy),
                new(yLength + uvLength, (picture.Stride >> sx) * bytes, ((picture.Geometry.Width + sx) >> sx) * bytes, (picture.Geometry.Height + sy) >> sy)];
        }
    }
}
