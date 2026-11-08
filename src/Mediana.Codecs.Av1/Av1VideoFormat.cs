// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using Mediana.Core;
using Mediana.Core.Video;

namespace Mediana.Codecs.Av1
{
    internal static class Av1VideoFormat
    {
        private static readonly VideoPixelFormat Gray8 = new("gray8", new VideoPlaneFormat(1, 1, 1));

        internal static VideoFormat Create(Av1FrameHeader header, EncodedStreamInfo stream)
        {
            Av1SequenceHeader geometry = header.PixelGeometry;
            VideoColorInfo color = new(CicpColorimetry.GetPrimaries(geometry.ColorPrimaries), CicpColorimetry.GetTransfer(geometry.TransferCharacteristics),
                CicpColorimetry.GetMatrix(geometry.MatrixCoefficients), geometry.FullRange ? ColorRange.Full : ColorRange.Limited,
                geometry.ChromaPosition switch
                {
                    0 => ChromaLocation.Unspecified,
                    1 => ChromaLocation.Left,
                    2 => ChromaLocation.TopLeft,
                    _ => throw new InvalidDataException("The AV1 chroma location is invalid.")
                });
            long numerator = (long)header.RenderWidth * geometry.Height;
            long denominator = (long)header.RenderHeight * geometry.Width;
            long divisor = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
            SampleAspectRatio aspect = new(checked((int)(numerator / divisor)), checked((int)(denominator / divisor)));
            return new(geometry.Width, geometry.Height, geometry.Monochrome ? Gray8 : VideoPixelFormat.Yuv420P8, color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        internal static PlaneLayout[] Layout(Av1SequenceHeader geometry)
        {
            if (geometry.Monochrome)
            {
                return [new(0, geometry.Stride, geometry.Width, geometry.Height)];
            }
            int chromaWidth = (geometry.Width + 1) >> 1;
            int chromaHeight = (geometry.Height + 1) >> 1;
            return [new(0, geometry.Stride, geometry.Width, geometry.Height),
                new(geometry.PlaneOffset(1), geometry.Stride >> 1, chromaWidth, chromaHeight),
                new(geometry.PlaneOffset(2), geometry.Stride >> 1, chromaWidth, chromaHeight)];
        }
    }
}
