// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal static class H264MixedPrediction
    {
        private const int MaximumBlockSize = 16;
        private const int LumaPadding = 5;
        private const int LumaFirstTap = 2;

        public static void Read(H264ReferencePicture reference, int plane, int x, int y, int width, int height,
            int motionX, int motionY, Span<byte> output)
        {
            if (width is < 1 or > MaximumBlockSize || height is < 1 or > MaximumBlockSize)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            H264Picture picture = reference.Picture;
            int horizontalScale = plane switch
            {
                0 => 1,
                1 or 2 => picture.ChromaHorizontalScale,
                _ => throw new ArgumentOutOfRangeException(nameof(plane))
            };
            int verticalScale = picture.VerticalScale(plane);
            width /= horizontalScale;
            height /= verticalScale;
            if (width * height * picture.SampleBytes != output.Length)
            {
                throw new ArgumentException("The H.264 prediction output must match its block dimensions.", nameof(output));
            }
            int horizontalShift = plane == 0 || picture.FullResolutionChroma ? 2 : 3;
            int verticalShift = plane == 0 || picture.FullResolutionChroma ? 2 : 2 + verticalScale / 2;
            long sourceX = (long)x / horizontalScale + (motionX >> horizontalShift);
            long sourceY = (long)y / verticalScale + (motionY >> verticalShift);
            int dx = motionX & ((1 << horizontalShift) - 1);
            int dy = motionY & ((1 << verticalShift) - 1);
            if (plane != 0 && !picture.FullResolutionChroma)
            {
                dy <<= 3 - verticalShift;
            }
            H264ReferencePlane source = new(reference, plane);
            if ((dx | dy) == 0)
            {
                source.Copy(sourceX, sourceY, width, height, output);
                return;
            }
            if (source.IsPaired)
            {
                PredictPaired(source, picture, plane, sourceX, sourceY, dx, dy, width, height, output);
            }
            else
            {
                Interpolate(source.Samples, source.Stride, source.Columns, source.Rows, picture, plane,
                    sourceX, sourceY, dx, dy, width, height, output);
            }
        }

        private static void PredictPaired(H264ReferencePlane source, H264Picture picture, int plane,
            long x, long y, int dx, int dy, int width, int height, Span<byte> output)
        {
            Span<byte> patch = stackalloc byte[(MaximumBlockSize + LumaPadding) * (MaximumBlockSize + LumaPadding) * sizeof(ushort)];
            int padding = plane == 0 || picture.FullResolutionChroma ? LumaPadding : 1;
            int first = plane == 0 || picture.FullResolutionChroma ? LumaFirstTap : 0;
            int columns = width + padding;
            int rows = height + padding;
            Span<byte> samples = patch[..(columns * rows * picture.SampleBytes)];
            source.Copy(x - first, y - first, columns, rows, samples);
            Interpolate(samples, columns, columns, rows, picture, plane, first, first, dx, dy, width, height, output);
        }

        private static void Interpolate(ReadOnlySpan<byte> samples, int stride, int columns, int rows,
            H264Picture picture, int plane, long x, long y, int dx, int dy, int width, int height, Span<byte> output)
        {
            if (picture.SampleBytes == 1)
            {
                if (plane == 0 || picture.FullResolutionChroma)
                {
                    H264LumaInterpolation.Apply(samples, stride, columns, rows, x, y, dx, dy, width, height, output);
                }
                else
                {
                    H264ChromaInterpolation.Apply(samples, stride, columns, rows, x, y, dx, dy, width, height, output);
                }
            }
            else
            {
                ReadOnlySpan<ushort> pixels = MemoryMarshal.Cast<byte, ushort>(samples);
                Span<ushort> destination = MemoryMarshal.Cast<byte, ushort>(output);
                if (plane == 0 || picture.FullResolutionChroma)
                {
                    H264HighPrecisionInterpolation.Luma(pixels, stride, columns, rows, x, y, dx, dy, width, height, destination, picture.MaximumSample);
                }
                else
                {
                    H264HighPrecisionInterpolation.Chroma(pixels, stride, columns, rows, x, y, dx, dy, width, height, destination);
                }
            }
        }
    }
}
