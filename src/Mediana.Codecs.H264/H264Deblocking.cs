// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

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
            int maximumQp = 0;
            foreach (byte qp in picture.Quantizers)
            {
                maximumQp = Math.Max(maximumQp, qp);
            }
            maximumQp = Math.Max(maximumQp, Math.Max(
                H264InverseTransform.ChromaQp(maximumQp, header.Picture.ChromaQpOffset),
                H264InverseTransform.ChromaQp(maximumQp, header.Picture.SecondChromaQpOffset)));
            if (Alpha[Math.Clamp(maximumQp + header.AlphaOffset, 0, 51)] == 0 ||
                Beta[Math.Clamp(maximumQp + header.BetaOffset, 0, 51)] == 0)
            {
                return;
            }
            int columns = header.Sequence.WidthInMbs;
            H264DeblockingContext context = new(picture, references, referencesL1, singleReference);
            Span<byte> strengths = stackalloc byte[32];
            for (int address = header.FirstMacroblock; address < endMacroblock; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int lumaX = address % columns * 16;
                int lumaY = address / columns * 16;
                context.BeginMacroblock(address);
                FillStrengths(picture, header, address, lumaX, lumaY, true, ref context, strengths[..16]);
                FillStrengths(picture, header, address, lumaX, lumaY, false, ref context, strengths[16..]);
                for (int plane = 0; plane < 3; plane++)
                {
                    int scale = plane == 0 ? 1 : 2;
                    int size = 16 / scale;
                    ApplyEdges(picture, header, plane, address, lumaX / scale, lumaY / scale, size, true, strengths[..16]);
                    ApplyEdges(picture, header, plane, address, lumaX / scale, lumaY / scale, size, false, strengths[16..]);
                }
            }
        }

        private static void FillStrengths(H264Picture picture, H264SliceHeader header, int address, int x, int y,
            bool vertical, ref H264DeblockingContext context,
            scoped Span<byte> strengths)
        {
            int edgeStep = picture.Transforms8x8[address] == 0 ? 4 : 8;
            int firstEdge = (vertical ? x == 0 : y == 0) ||
                (header.DeblockingIdc == 2 && address - (vertical ? 1 : header.Sequence.WidthInMbs) < header.FirstMacroblock)
                ? edgeStep : 0;
            context.FillStrengths(x, y, vertical, firstEdge, edgeStep, strengths);
        }

        private static void ApplyEdges(H264Picture picture, H264SliceHeader header, int plane,
            int address, int x, int y, int size, bool vertical, ReadOnlySpan<byte> strengths)
        {
            if (!strengths.ContainsAnyExcept((byte)0))
            {
                return;
            }
            int stride = picture.Stride(plane);
            int step = vertical ? 1 : stride;
            Span<byte> pixels = picture.Plane(plane);
            int scale = plane == 0 ? 1 : 2;
            int currentQp = Qp(picture, header.Picture, plane, address);
            for (int edge = 0; edge < size; edge += 4)
            {
                ReadOnlySpan<byte> edgeStrengths = strengths.Slice(edge * scale, 4);
                if (!edgeStrengths.ContainsAnyExcept((byte)0))
                {
                    continue;
                }
                int previousQp = edge == 0
                    ? Qp(picture, header.Picture, plane, address - (vertical ? 1 : header.Sequence.WidthInMbs)) : currentQp;
                int averageQp = (previousQp + currentQp + 1) >> 1;
                int indexA = Math.Clamp(averageQp + header.AlphaOffset, 0, 51);
                int indexB = Math.Clamp(averageQp + header.BetaOffset, 0, 51);
                int alpha = Alpha[indexA];
                int beta = Beta[indexB];
                if (alpha == 0 || beta == 0)
                {
                    continue;
                }
                int start = y * stride + x + edge * step;
                H264DeblockingThresholds thresholds = new(alpha, beta, Tc1[indexA], Tc2[indexA], Tc3[indexA]);
                H264DeblockingFilter.Apply(pixels, start, stride, vertical, plane != 0, edgeStrengths, thresholds);
            }
        }

        internal static int BoundaryStrength(H264Picture picture, int x, int y, bool vertical,
            bool macroblockEdge, IReadOnlyList<H264ReferencePicture?>? references, IReadOnlyList<H264ReferencePicture?>? referencesL1 = null)
        {
            H264DeblockingContext context = new(picture, references, referencesL1);
            context.BeginMacroblock(y / 16 * picture.Sequence.WidthInMbs + x / 16);
            return context.Strength(x, y, vertical, macroblockEdge);
        }

        private static int Qp(H264Picture picture, H264PictureParameterSet parameters, int plane, int address) => plane switch
        {
            0 => picture.Quantizers[address],
            1 => H264InverseTransform.ChromaQp(picture.Quantizers[address], parameters.ChromaQpOffset),
            2 => H264InverseTransform.ChromaQp(picture.Quantizers[address], parameters.SecondChromaQpOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

    }
}
