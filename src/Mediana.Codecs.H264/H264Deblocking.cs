// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal static class H264Deblocking
    {
        // Rec. ITU-T H.264, Tables 8-16 and 8-17 (8-bit samples).
        private static ReadOnlySpan<byte> Alpha =>
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 4, 5, 6, 7, 8, 9, 10, 12, 13,
                15, 17, 20, 22, 25, 28, 32, 36, 40, 45, 50, 56, 63, 71, 80, 90, 101, 113, 127, 144, 162, 182, 203, 226, 255, 255];
        private static ReadOnlySpan<byte> Beta =>
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4,
                6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18, 18];
        private static ReadOnlySpan<byte> Tc1 =>
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 6, 6, 7, 8, 9, 10, 11, 13];
        private static ReadOnlySpan<byte> Tc2 =>
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 5, 5, 6, 7, 8, 8, 10, 11, 12, 13, 15, 17];
        private static ReadOnlySpan<byte> Tc3 =>
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 6, 6, 7, 8, 9, 10, 11, 13, 14, 16, 18, 20, 23, 25];

        public static void Apply(H264Picture picture, H264SliceHeader header, CancellationToken cancellationToken,
            IReadOnlyList<H264ReferencePicture?>? references = null, IReadOnlyList<H264ReferencePicture?>? referencesL1 = null) =>
            ApplyRange(picture, header, picture.Quantizers.Length, cancellationToken, references, referencesL1);

        public static void ApplySingleReference(H264Picture picture, H264SliceHeader header, H264PictureIdentity? reference,
            CancellationToken cancellationToken) =>
            ApplyRange(picture, header, picture.Quantizers.Length, cancellationToken, null, null, reference);

        public static void Apply(H264Picture picture, IReadOnlyList<H264SliceRegion> slices, CancellationToken cancellationToken)
        {
            foreach (H264SliceRegion slice in slices)
            {
                ApplyRange(picture, slice.Header, slice.EndMacroblock, cancellationToken, null, null);
            }
        }

        private static void ApplyRange(H264Picture picture, H264SliceHeader header, int endMacroblock, CancellationToken cancellationToken,
            IReadOnlyList<H264ReferencePicture?>? references, IReadOnlyList<H264ReferencePicture?>? referencesL1,
            H264PictureIdentity? singleReference = null)
        {
            if (!header.DeblockingEnabled)
            {
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> quantizers = picture.Quantizers;
            int firstFilterable = FirstFilterableQuantizer(picture, header);
            if (!quantizers.ContainsAnyInRange((byte)firstFilterable, byte.MaxValue))
            {
                return;
            }
            int columns = header.Sequence.WidthInMbs;
            H264DeblockingContext context = new(picture, references, referencesL1, singleReference);
            if (picture.Layout.MacroblockAdaptiveFrameField)
            {
                H264AdaptiveDeblocking.Apply(picture, header, endMacroblock, ref context, cancellationToken);
                return;
            }
            Span<byte> strengths = stackalloc byte[32];
            for (int address = header.FirstMacroblock; address < endMacroblock; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int lumaX = address % columns * 16;
                if (quantizers[address] < firstFilterable
                    && (lumaX == 0 || quantizers[address - 1] < firstFilterable)
                    && (address < columns || quantizers[address - columns] < firstFilterable))
                {
                    continue;
                }
                int lumaY = address / columns * 16;
                context.BeginMacroblock(address);
                FillStrengths(picture, header, address, lumaX, lumaY, true, ref context, strengths[..16]);
                FillStrengths(picture, header, address, lumaX, lumaY, false, ref context, strengths[16..]);
                for (int plane = 0; plane < 3; plane++)
                {
                    int scale = picture.HorizontalScale(plane);
                    int verticalScale = picture.VerticalScale(plane);
                    ApplyEdges(picture, header, plane, address, lumaX / scale, lumaY / verticalScale, true, strengths[..16]);
                    ApplyEdges(picture, header, plane, address, lumaX / scale, lumaY / verticalScale, false, strengths[16..]);
                }
            }
        }

        private static int FirstFilterableQuantizer(H264Picture picture, H264SliceHeader header)
        {
            int chromaOffset = Math.Max(header.Picture.ChromaQpOffset, header.Picture.SecondChromaQpOffset);
            for (int qp = 0; qp <= picture.LumaQuantizer.Maximum; qp++)
            {
                int luma = qp - picture.LumaQuantizer.Offset;
                int chroma = picture.ChromaQuantizer.ChromaValue(luma, chromaOffset) - picture.ChromaQuantizer.Offset;
                int highest = Math.Max(luma, chroma);
                if (Alpha[Math.Clamp(highest + header.AlphaOffset, 0, 51)] != 0
                    && Beta[Math.Clamp(highest + header.BetaOffset, 0, 51)] != 0)
                {
                    return qp;
                }
            }
            return picture.LumaQuantizer.Maximum + 1;
        }

        private static void FillStrengths(H264Picture picture, H264SliceHeader header, int address, int x, int y,
            bool vertical, ref H264DeblockingContext context,
            scoped Span<byte> strengths)
        {
            int edgeStep = !picture.Uses8x8Transform(address) || picture.Sequence.ChromaFormat == 2 ? 4 : 8;
            int firstEdge = (vertical ? x == 0 : y == 0) ||
                (header.DeblockingIdc == 2 && address - (vertical ? 1 : header.Sequence.WidthInMbs) < header.FirstMacroblock)
                ? edgeStep : 0;
            context.FillStrengths(x, y, vertical, firstEdge, edgeStep, strengths);
        }

        private static void ApplyEdges(H264Picture picture, H264SliceHeader header, int plane,
            int address, int x, int y, bool vertical, ReadOnlySpan<byte> strengths)
        {
            if (!strengths.ContainsAnyExcept((byte)0))
            {
                return;
            }
            int stride = picture.Stride(plane);
            int step = vertical ? 1 : stride;
            Span<byte> pixels = picture.Plane(plane);
            int horizontalScale = picture.HorizontalScale(plane);
            int verticalScale = picture.VerticalScale(plane);
            int scale = vertical ? horizontalScale : verticalScale;
            int size = 16 / scale;
            int segmentLength = 4 / (vertical ? verticalScale : horizontalScale);
            int edgeStep = (plane == 0 || picture.FullResolutionChroma) && picture.Uses8x8Transform(address) ? 8 : 4;
            int currentQp = Qp(picture, header.Picture, plane, address);
            for (int edge = 0; edge < size; edge += edgeStep)
            {
                ReadOnlySpan<byte> edgeStrengths = strengths.Slice(edge * scale, 4);
                if (!edgeStrengths.ContainsAnyExcept((byte)0))
                {
                    continue;
                }
                int previousQp = edge == 0
                    ? Qp(picture, header.Picture, plane, address - (vertical ? 1 : header.Sequence.WidthInMbs)) : currentQp;
                H264DeblockingThresholds thresholds = Thresholds(previousQp, currentQp, header);
                if (thresholds.Alpha == 0 || thresholds.Beta == 0)
                {
                    continue;
                }
                int start = y * stride + x + edge * step;
                if (picture.SampleBytes == 1)
                {
                    H264DeblockingFilter.Apply(pixels, start, stride, vertical, plane != 0 && !picture.FullResolutionChroma, segmentLength, edgeStrengths, thresholds);
                }
                else
                {
                    H264DeblockingFilter.ApplyScalar<ushort>(MemoryMarshal.Cast<byte, ushort>(pixels), start, stride,
                        vertical, plane != 0 && !picture.FullResolutionChroma, segmentLength, edgeStrengths, thresholds, picture.MaximumSample);
                }
            }
        }

        internal static int BoundaryStrength(H264Picture picture, int x, int y, bool vertical,
            bool macroblockEdge, IReadOnlyList<H264ReferencePicture?>? references, IReadOnlyList<H264ReferencePicture?>? referencesL1 = null)
        {
            H264DeblockingContext context = new(picture, references, referencesL1);
            context.BeginMacroblock(y / 16 * picture.Sequence.WidthInMbs + x / 16);
            return context.Strength(x, y, vertical, macroblockEdge);
        }

        internal static H264DeblockingThresholds Thresholds(H264Picture picture, H264SliceHeader header, int plane,
            int previousAddress, int currentAddress)
        {
            return Thresholds(Qp(picture, header.Picture, plane, previousAddress),
                Qp(picture, header.Picture, plane, currentAddress), header);
        }

        private static H264DeblockingThresholds Thresholds(int previousQp, int currentQp, H264SliceHeader header)
        {
            int average = (previousQp + currentQp + 1) >> 1;
            int indexA = Math.Clamp(average + header.AlphaOffset, 0, 51);
            int indexB = Math.Clamp(average + header.BetaOffset, 0, 51);
            int shift = header.Sequence.LumaBitDepth - 8;
            return new(Alpha[indexA] << shift, Beta[indexB] << shift, Tc1[indexA] << shift, Tc2[indexA] << shift, Tc3[indexA] << shift);
        }

        private static int Qp(H264Picture picture, H264PictureParameterSet parameters, int plane, int address) => plane switch
        {
            0 => picture.Quantizers[address] - picture.LumaQuantizer.Offset,
            1 => picture.ChromaQuantizer.ChromaValue(picture.Quantizers[address] - picture.LumaQuantizer.Offset, parameters.ChromaQpOffset) - picture.ChromaQuantizer.Offset,
            2 => picture.ChromaQuantizer.ChromaValue(picture.Quantizers[address] - picture.LumaQuantizer.Offset, parameters.SecondChromaQpOffset) - picture.ChromaQuantizer.Offset,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

    }
}
