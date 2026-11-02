// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264DeblockingFilter
    {
        public static void Apply(Span<byte> pixels, int start, int stride, bool vertical, bool chroma,
            ReadOnlySpan<byte> strengths, in H264DeblockingThresholds thresholds)
        {
            if (Sse2.IsSupported)
            {
                int segmentLength = chroma ? 2 : 4;
                int size = 4 * segmentLength;
                for (int offset = 0; offset < size; offset += 8)
                {
                    int segment = offset / segmentLength;
                    short first = strengths[segment];
                    short second = strengths[segment + 1];
                    short firstClip = thresholds.Clipping(first);
                    short secondClip = thresholds.Clipping(second);
                    Vector128<short> boundary;
                    Vector128<short> clipping;
                    if (chroma)
                    {
                        short third = strengths[2];
                        short fourth = strengths[3];
                        short thirdClip = thresholds.Clipping(third);
                        short fourthClip = thresholds.Clipping(fourth);
                        boundary = Vector128.Create(first, first, second, second, third, third, fourth, fourth);
                        clipping = Vector128.Create(firstClip, firstClip, secondClip, secondClip, thirdClip, thirdClip, fourthClip, fourthClip);
                    }
                    else
                    {
                        boundary = Vector128.Create(first, first, first, first, second, second, second, second);
                        clipping = Vector128.Create(firstClip, firstClip, firstClip, firstClip, secondClip, secondClip, secondClip, secondClip);
                    }
                    int position = start + offset * (vertical ? stride : 1);
                    H264DeblockingSamples samples = H264DeblockingVector.Read(pixels, position, stride, vertical);
                    if (H264DeblockingVector.Filter(ref samples, boundary, clipping, thresholds, chroma))
                    {
                        H264DeblockingVector.Write(pixels, position, stride, vertical, samples);
                    }
                }
                return;
            }
            int step = vertical ? 1 : stride;
            int along = vertical ? stride : 1;
            int length = chroma ? 2 : 4;
            for (int segment = 0; segment < 4; segment++)
            {
                int strength = strengths[segment];
                int tc = thresholds.Clipping(strength);
                if (strength == 0)
                {
                    continue;
                }
                for (int offset = 0; offset < length; offset++)
                {
                    Filter(pixels, start + (segment * length + offset) * along, step,
                        thresholds.Alpha, thresholds.Beta, tc, strength == 4, chroma);
                }
            }
        }

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
