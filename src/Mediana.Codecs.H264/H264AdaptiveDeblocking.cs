// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal static class H264AdaptiveDeblocking
    {
        public static void Apply(H264Picture picture, H264SliceHeader header, int endMacroblock,
            ref H264DeblockingContext context, CancellationToken cancellationToken)
        {
            for (int address = header.FirstMacroblock; address < endMacroblock; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int plane = 0; plane < 3; plane++)
                {
                    if (picture.SampleBytes == 1)
                    {
                        ApplyMacroblock(picture, header, address, plane, picture.Plane(plane), ref context);
                    }
                    else
                    {
                        ApplyMacroblock(picture, header, address, plane, MemoryMarshal.Cast<byte, ushort>(picture.Plane(plane)), ref context);
                    }
                }
            }
        }

        private static void ApplyMacroblock<TSample>(H264Picture picture, H264SliceHeader header, int address, int plane,
            Span<TSample> pixels, ref H264DeblockingContext context) where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int width = 16 / picture.HorizontalScale(plane);
            int height = 16 / picture.VerticalScale(plane);
            (int x, int y, int rowStep) = picture.Grid.PixelOrigin(address, width, height);
            int edgeStep = (plane == 0 || picture.FullResolutionChroma) && picture.Transforms8x8[address] != 0 ? 8 : 4;
            for (int edge = 0; edge < width; edge += edgeStep)
            {
                FilterEdge(picture, header, address, plane, pixels, x + edge, y, rowStep, true, ref context);
            }
            bool doubleTop = (address & 1) == 0 && rowStep == 1 && address >= 2 * picture.Sequence.WidthInMbs &&
                picture.Grid.IsField(address - 2 * picture.Sequence.WidthInMbs);
            if (doubleTop)
            {
                FilterEdge(picture, header, address, plane, pixels, x, y, 2, false, ref context);
                FilterEdge(picture, header, address, plane, pixels, x, y + 1, 2, false, ref context);
            }
            for (int edge = doubleTop ? edgeStep : 0; edge < height; edge += edgeStep)
            {
                FilterEdge(picture, header, address, plane, pixels, x, y + edge * rowStep, rowStep, false, ref context);
            }
        }

        private static void FilterEdge<TSample>(H264Picture picture, H264SliceHeader header, int address, int plane,
            Span<TSample> pixels, int x, int y, int rowStep, bool vertical, ref H264DeblockingContext context)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int horizontalScale = picture.HorizontalScale(plane);
            int verticalScale = picture.VerticalScale(plane);
            int width = 16 / horizontalScale;
            int height = 16 / verticalScale;
            int length = vertical ? height : width;
            int stride = picture.Stride(plane);
            int step = vertical ? 1 : stride * rowStep;
            H264NeighbourLocation? first = picture.Grid.SampleLocation(x - (vertical ? 1 : 0),
                y - (vertical ? 0 : rowStep), width, height);
            if (first is not H264NeighbourLocation firstNeighbour ||
                (header.DeblockingIdc == 2 && firstNeighbour.Address < header.FirstMacroblock))
            {
                return;
            }
            if (picture.Grid.IsField(firstNeighbour.Address) == picture.Grid.IsField(address))
            {
                FilterRegularEdge(picture, header, address, plane, pixels, x, y, rowStep, vertical, firstNeighbour.Address, ref context);
                return;
            }
            for (int sample = 0; sample < length; sample++)
            {
                int qx = vertical ? x : x + sample;
                int qy = vertical ? y + sample * rowStep : y;
                H264NeighbourLocation? previous = picture.Grid.SampleLocation(qx - (vertical ? 1 : 0),
                    qy - (vertical ? 0 : rowStep), width, height);
                if (previous is not H264NeighbourLocation p ||
                    (header.DeblockingIdc == 2 && p.Address < header.FirstMacroblock))
                {
                    continue;
                }
                H264NeighbourLocation q = picture.Grid.SampleLocation(qx, qy, width, height)
                    ?? throw new InvalidDataException("An adaptive deblocking edge exceeds the picture.");
                if (q.Address != address)
                {
                    throw new InvalidDataException("An adaptive deblocking edge left its macroblock.");
                }
                H264NeighbourLocation lumaP = new(p.Address, p.X * horizontalScale, p.Y * verticalScale);
                H264NeighbourLocation lumaQ = new(q.Address, q.X * horizontalScale, q.Y * verticalScale);
                int strength = context.AdaptiveStrength(lumaP, lumaQ, vertical);
                if (strength == 0)
                {
                    continue;
                }
                H264DeblockingThresholds thresholds = H264Deblocking.Thresholds(picture, header, plane, p.Address, q.Address);
                H264DeblockingFilter.Filter(pixels, qy * stride + qx, step, thresholds.Alpha, thresholds.Beta,
                    thresholds.Clipping(strength), strength == 4, plane != 0 && !picture.FullResolutionChroma, picture.MaximumSample);
            }
        }

        private static void FilterRegularEdge<TSample>(H264Picture picture, H264SliceHeader header, int address, int plane,
            Span<TSample> pixels, int x, int y, int rowStep, bool vertical, int previousAddress, ref H264DeblockingContext context)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int horizontalScale = picture.HorizontalScale(plane);
            int verticalScale = picture.VerticalScale(plane);
            int segmentLength = 4 / (vertical ? verticalScale : horizontalScale);
            Span<byte> strengths = stackalloc byte[4];
            for (int segment = 0; segment < 4; segment++)
            {
                int qx = vertical ? x : x + segment * segmentLength;
                int qy = vertical ? y + segment * segmentLength * rowStep : y;
                H264NeighbourLocation p = picture.Grid.SampleLocation(qx - (vertical ? 1 : 0),
                    qy - (vertical ? 0 : rowStep), 16 / horizontalScale, 16 / verticalScale)
                    ?? throw new InvalidDataException("An adaptive deblocking edge has no preceding samples.");
                H264NeighbourLocation q = picture.Grid.SampleLocation(qx, qy, 16 / horizontalScale, 16 / verticalScale)
                    ?? throw new InvalidDataException("An adaptive deblocking edge exceeds the picture.");
                H264NeighbourLocation lumaP = new(p.Address, p.X * horizontalScale, p.Y * verticalScale);
                H264NeighbourLocation lumaQ = new(q.Address, q.X * horizontalScale, q.Y * verticalScale);
                strengths[segment] = (byte)context.AdaptiveStrength(lumaP, lumaQ, vertical);
            }
            if (!strengths.ContainsAnyExcept((byte)0))
            {
                return;
            }
            H264DeblockingThresholds thresholds = H264Deblocking.Thresholds(picture, header, plane, previousAddress, address);
            int stride = picture.Stride(plane);
            if (typeof(TSample) == typeof(byte))
            {
                H264DeblockingFilter.Apply(MemoryMarshal.Cast<TSample, byte>(pixels), y * stride + x,
                    stride * rowStep, vertical, plane != 0 && !picture.FullResolutionChroma, segmentLength, strengths, thresholds);
            }
            else
            {
                H264DeblockingFilter.ApplyScalar(pixels, y * stride + x, stride * rowStep, vertical,
                    plane != 0 && !picture.FullResolutionChroma, segmentLength, strengths, thresholds, picture.MaximumSample);
            }
        }
    }
}
