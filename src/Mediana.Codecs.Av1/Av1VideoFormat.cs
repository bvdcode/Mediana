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
            VideoColorInfo color = ReadColor(geometry);
            SampleAspectRatio aspect = ReadAspectRatio(geometry, header.RenderWidth, header.RenderHeight);
            return new(geometry.Width, geometry.Height, ReadPixelFormat(geometry), color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        internal static SampleAspectRatio ReadAspectRatio(Av1SequenceHeader geometry, int renderWidth, int renderHeight)
        {
            long numerator = (long)renderWidth * geometry.Height;
            long denominator = (long)renderHeight * geometry.Width;
            long divisor = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
            return new(checked((int)(numerator / divisor)), checked((int)(denominator / divisor)));
        }

        internal static VideoColorInfo ReadColor(Av1SequenceHeader geometry) =>
            new(CicpColorimetry.GetPrimaries(geometry.ColorPrimaries), CicpColorimetry.GetTransfer(geometry.TransferCharacteristics),
                CicpColorimetry.GetMatrix(geometry.MatrixCoefficients), geometry.FullRange ? ColorRange.Full : ColorRange.Limited,
                geometry.ChromaPosition switch
                {
                    0 => ChromaLocation.Unspecified,
                    1 => ChromaLocation.Left,
                    2 => ChromaLocation.TopLeft,
                    _ => throw new InvalidDataException("The AV1 chroma location is invalid.")
                });

        internal static VideoPixelFormat ReadPixelFormat(Av1SequenceHeader geometry) =>
            (geometry.Monochrome, geometry.BitDepth) switch
            {
                (true, 8) => Gray8,
                (true, 10) => Gray10,
                (false, 8) => VideoPixelFormat.Yuv420P8,
                (false, 10) => VideoPixelFormat.Yuv420P10,
                _ => throw new NotSupportedException("AV1 reconstruction requires eight- or ten-bit Main-profile samples.")
            };

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
