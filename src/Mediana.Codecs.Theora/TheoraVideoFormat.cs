// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora
{
    internal static class TheoraVideoFormat
    {
        internal static VideoFormat Create(TheoraIdentification info, EncodedStreamInfo stream)
        {
            VideoPixelFormat pixels = info.PixelFormat switch
            {
                0 => VideoPixelFormat.Yuv420P8,
                2 => new PlanarVideoPixelFormat("yuv422p8", PlanarColorModel.Yuv, 8, 2, 1),
                3 => new PlanarVideoPixelFormat("yuv444p8", PlanarColorModel.Yuv, 8),
                _ => throw new InvalidDataException("Invalid Theora pixel format.")
            };
            VideoColorInfo color = info.ColorSpace switch
            {
                1 => new(ColorPrimaries.Bt470M, TransferFunction.Bt709, MatrixCoefficients.Bt601),
                2 => new(ColorPrimaries.Bt470Bg, TransferFunction.Bt709, MatrixCoefficients.Bt601),
                0 => new(),
                _ => throw new NotSupportedException("The Theora color-space identifier is reserved.")
            };
            color = color with { Range = ColorRange.Limited, ChromaLocation = ChromaLocation.Center };
            SampleAspectRatio aspect = info.AspectRatioNumerator == 0 || info.AspectRatioDenominator == 0 ? SampleAspectRatio.Square
                : new(info.AspectRatioNumerator, info.AspectRatioDenominator);
            return new(info.Width, info.Height, pixels, color, stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        internal static PlaneLayout[] Planes(TheoraIdentification info)
        {
            PlaneLayout[] planes = new PlaneLayout[3]; int offset = 0;
            for (int plane = 0; plane < 3; plane++)
            {
                int hs = plane == 0 ? 1 : info.HorizontalSubsampling, vs = plane == 0 ? 1 : info.VerticalSubsampling;
                int stride = info.CodedWidth / hs, rows = info.CodedHeight / vs;
                int x = info.PictureX / hs, y = (info.CodedHeight - info.PictureY - info.Height) / vs;
                planes[plane] = new(offset + y * stride + x, stride, (info.Width + hs - 1) / hs, (info.Height + vs - 1) / vs);
                offset = checked(offset + stride * rows);
            }
            return planes;
        }
    }
}
