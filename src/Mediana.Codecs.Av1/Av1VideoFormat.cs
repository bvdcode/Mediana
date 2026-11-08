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
        private static readonly VideoPixelFormat Gray10 = new("gray10", new VideoPlaneFormat(1, 1, 2));

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
            VideoPixelFormat pixels = (geometry.Monochrome, geometry.BitDepth) switch
            {
                (true, 8) => Gray8,
                (true, 10) => Gray10,
                (false, 8) => VideoPixelFormat.Yuv420P8,
                (false, 10) => VideoPixelFormat.Yuv420P10,
                _ => throw new NotSupportedException("AV1 reconstruction requires eight- or ten-bit Main-profile samples.")
            };
            return new(geometry.Width, geometry.Height, pixels, color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        internal static PlaneLayout[] Layout(Av1SequenceHeader geometry)
        {
            int sampleBytes = geometry.BitDepth switch
            {
                8 => 1,
                10 => 2,
                _ => throw new NotSupportedException("AV1 reconstruction requires eight- or ten-bit Main-profile samples.")
            };
            int stride = checked(geometry.Stride * sampleBytes);
            int rowBytes = checked(geometry.Width * sampleBytes);
            if (geometry.Monochrome)
            {
                return [new(0, stride, rowBytes, geometry.Height)];
            }
            int chromaWidth = checked(((geometry.Width + 1) >> 1) * sampleBytes);
            int chromaHeight = (geometry.Height + 1) >> 1;
            return [new(0, stride, rowBytes, geometry.Height),
                new(checked(geometry.PlaneOffset(1) * sampleBytes), stride >> 1, chromaWidth, chromaHeight),
                new(checked(geometry.PlaneOffset(2) * sampleBytes), stride >> 1, chromaWidth, chromaHeight)];
        }
    }
}
