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

        public static void Apply(H264Picture picture, IReadOnlyList<H264SliceRegion> slices, CancellationToken cancellationToken)
        {
            foreach (H264SliceRegion slice in slices)
            {
                ApplyRange(picture, slice.Header, slice.EndMacroblock, cancellationToken, null, null);
            }
        }

        private static void ApplyRange(H264Picture picture, H264SliceHeader header, int endMacroblock, CancellationToken cancellationToken,
            IReadOnlyList<H264ReferencePicture?>? references, IReadOnlyList<H264ReferencePicture?>? referencesL1)
        {
            if (!header.DeblockingEnabled)
            {
                return;
            }
            int columns = header.Sequence.WidthInMbs;
            for (int address = header.FirstMacroblock; address < endMacroblock; address++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int plane = 0; plane < 3; plane++)
                {
                    int size = plane == 0 ? 16 : 8;
                    int x = (address % columns) * size;
                    int y = (address / columns) * size;
                    ApplyEdges(picture, header, plane, address, x, y, size, true, references, referencesL1);
                    ApplyEdges(picture, header, plane, address, x, y, size, false, references, referencesL1);
                }
            }
        }

        private static void ApplyEdges(H264Picture picture, H264SliceHeader header, int plane,
            int address, int x, int y, int size, bool vertical, IReadOnlyList<H264ReferencePicture?>? references,
            IReadOnlyList<H264ReferencePicture?>? referencesL1)
        {
            int stride = picture.Stride(plane);
            int step = vertical ? 1 : stride;
            int along = vertical ? stride : 1;
            Span<byte> pixels = picture.Plane(plane);
            int scale = plane == 0 ? 1 : 2;
            int segmentLength = 4 / scale;
            int currentQp = Qp(picture, header.Picture, plane, address);
            for (int edge = 0; edge < size; edge += 4)
            {
                if (plane == 0 && edge % 8 != 0 && picture.Transforms8x8[address] != 0)
                {
                    continue;
                }
                if (edge == 0 && ((vertical ? x == 0 : y == 0) ||
                    (header.DeblockingIdc == 2 && address - (vertical ? 1 : header.Sequence.WidthInMbs) < header.FirstMacroblock)))
                {
                    continue;
                }
                int previousQp = edge == 0
                    ? Qp(picture, header.Picture, plane, address - (vertical ? 1 : header.Sequence.WidthInMbs)) : currentQp;
                int averageQp = (previousQp + currentQp + 1) >> 1;
                int indexA = Math.Clamp(averageQp + header.AlphaOffset, 0, 51);
                int indexB = Math.Clamp(averageQp + header.BetaOffset, 0, 51);
                int start = y * stride + x + edge * step;
                for (int sample = 0; sample < size; sample += segmentLength)
                {
                    int lumaX = (x + (vertical ? edge : sample)) * scale;
                    int lumaY = (y + (vertical ? sample : edge)) * scale;
                    int strength = BoundaryStrength(picture, lumaX, lumaY, vertical, edge == 0, references, referencesL1);
                    int tc = strength switch
                    {
                        0 or 4 => 0,
                        1 => Tc1[indexA],
                        2 => Tc2[indexA],
                        3 => Tc3[indexA],
                        _ => throw new InvalidOperationException("Invalid H.264 boundary strength.")
                    };
                    if (strength != 0)
                    {
                        for (int offset = 0; offset < segmentLength; offset++)
                        {
                            Filter(pixels, start + (sample + offset) * along, step, Alpha[indexA], Beta[indexB],
                                tc, strength == 4, plane != 0);
                        }
                    }
                }
            }
        }

        internal static int BoundaryStrength(H264Picture picture, int x, int y, bool vertical,
            bool macroblockEdge, IReadOnlyList<H264ReferencePicture?>? references, IReadOnlyList<H264ReferencePicture?>? referencesL1 = null)
        {
            int previous = picture.BlockIndex(0, x - (vertical ? 1 : 0), y - (vertical ? 0 : 1));
            int current = picture.BlockIndex(0, x, y);
            if (picture.Modes[previous] != H264Picture.InterMode || picture.Modes[current] != H264Picture.InterMode)
            {
                return macroblockEdge ? 4 : 3;
            }
            if (HasResidual(picture, x - (vertical ? 1 : 0), y - (vertical ? 0 : 1)) || HasResidual(picture, x, y))
            {
                return 2;
            }
            return H264DeblockingMotion.Strength(picture, previous, current, references, referencesL1);
        }

        private static bool HasResidual(H264Picture picture, int x, int y)
        {
            int address = y / 16 * picture.Sequence.WidthInMbs + x / 16;
            if (picture.Transforms8x8[address] == 0)
            {
                return picture.Counts(0)[picture.BlockIndex(0, x, y)] != 0;
            }
            int position = picture.BlockIndex(0, x / 8 * 8, y / 8 * 8);
            int stride = picture.Width / 4;
            Span<byte> counts = picture.Counts(0);
            return counts[position] != 0 || counts[position + 1] != 0 || counts[position + stride] != 0 || counts[position + stride + 1] != 0;
        }

        private static int Qp(H264Picture picture, H264PictureParameterSet parameters, int plane, int address) => plane switch
        {
            0 => picture.Quantizers[address],
            1 => H264InverseTransform.ChromaQp(picture.Quantizers[address], parameters.ChromaQpOffset),
            2 => H264InverseTransform.ChromaQp(picture.Quantizers[address], parameters.SecondChromaQpOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

        private static void Filter(Span<byte> pixels, int position, int step, int alpha, int beta,
            int tc0, bool strong, bool chroma)
        {
            int p0 = pixels[position - step];
            int p1 = pixels[position - 2 * step];
            int p2 = pixels[position - 3 * step];
            int q0 = pixels[position];
            int q1 = pixels[position + step];
            int q2 = pixels[position + 2 * step];
            int difference = Math.Abs(p0 - q0);
            if (difference >= alpha || Math.Abs(p1 - p0) >= beta || Math.Abs(q1 - q0) >= beta)
            {
                return;
            }
            bool ap = Math.Abs(p2 - p0) < beta;
            bool aq = Math.Abs(q2 - q0) < beta;
            if (strong)
            {
                bool wide = !chroma && difference < ((alpha >> 2) + 2);
                if (wide && ap)
                {
                    int p3 = pixels[position - 4 * step];
                    pixels[position - step] = (byte)((p2 + 2 * p1 + 2 * p0 + 2 * q0 + q1 + 4) >> 3);
                    pixels[position - 2 * step] = (byte)((p2 + p1 + p0 + q0 + 2) >> 2);
                    pixels[position - 3 * step] = (byte)((2 * p3 + 3 * p2 + p1 + p0 + q0 + 4) >> 3);
                }
                else
                {
                    pixels[position - step] = (byte)((2 * p1 + p0 + q1 + 2) >> 2);
                }
                if (wide && aq)
                {
                    int q3 = pixels[position + 3 * step];
                    pixels[position] = (byte)((p1 + 2 * p0 + 2 * q0 + 2 * q1 + q2 + 4) >> 3);
                    pixels[position + step] = (byte)((p0 + q0 + q1 + q2 + 2) >> 2);
                    pixels[position + 2 * step] = (byte)((2 * q3 + 3 * q2 + q1 + q0 + p0 + 4) >> 3);
                }
                else
                {
                    pixels[position] = (byte)((2 * q1 + q0 + p1 + 2) >> 2);
                }
                return;
            }
            int tc = chroma ? tc0 + 1 : tc0 + (ap ? 1 : 0) + (aq ? 1 : 0);
            int delta = Math.Clamp((((q0 - p0) << 2) + p1 - q1 + 4) >> 3, -tc, tc);
            pixels[position - step] = (byte)Math.Clamp(p0 + delta, 0, 255);
            pixels[position] = (byte)Math.Clamp(q0 - delta, 0, 255);
            if (!chroma && ap)
            {
                pixels[position - 2 * step] = (byte)(p1 + Math.Clamp((p2 + ((p0 + q0 + 1) >> 1) - 2 * p1) >> 1, -tc0, tc0));
            }
            if (!chroma && aq)
            {
                pixels[position + step] = (byte)(q1 + Math.Clamp((q2 + ((p0 + q0 + 1) >> 1) - 2 * q1) >> 1, -tc0, tc0));
            }
        }
    }
}
