// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1DirectionalPrediction
    {
        private static void PrepareEdges(Av1TransformSize size, Av1IntraEdges edges, int angle, bool enabled, out int above, out int left)
        {
            above = left = 0;
            if (!enabled || angle is 90 or 180) { return; }
            if (angle is > 90 and < 180 && size.Width + size.Height >= 24)
            {
                byte corner = (byte)((edges.Left[2] * 5 + edges.Above[1] * 6 + edges.Above[2] * 5 + 8) >> 4);
                edges.Left[1] = edges.Above[1] = corner;
            }
            if (edges.HaveAbove)
            {
                FilterEdge(edges.Above[1..], edges.Width + (angle < 90 ? size.Height : 0) + 1,
                    Strength(size, angle - 90, edges.SmoothNeighbors));
            }
            if (edges.HaveLeft)
            {
                FilterEdge(edges.Left[1..], edges.Height + (angle > 180 ? size.Width : 0) + 1,
                    Strength(size, angle - 180, edges.SmoothNeighbors));
            }
            if (UseUpsample(size, angle - 90, edges.SmoothNeighbors))
            {
                above = 1;
                Upsample(edges.Above, size.Width + (angle < 90 ? size.Height : 0));
            }
            if (UseUpsample(size, angle - 180, edges.SmoothNeighbors))
            {
                left = 1;
                Upsample(edges.Left, size.Height + (angle > 180 ? size.Width : 0));
            }
        }

        private static int Strength(Av1TransformSize size, int delta, bool smooth)
        {
            int difference = Math.Abs(delta);
            int extent = size.Width + size.Height;
            if (!smooth)
            {
                if (extent <= 8) { return difference >= 56 ? 1 : 0; }
                if (extent <= 16) { return difference >= 40 ? 1 : 0; }
                if (extent <= 24)
                {
                    if (difference >= 32) { return 3; }
                    if (difference >= 16) { return 2; }
                    return difference >= 8 ? 1 : 0;
                }
                if (extent <= 32)
                {
                    if (difference >= 32) { return 3; }
                    return difference >= 4 ? 2 : 1;
                }
                return 3;
            }
            if (extent <= 8)
            {
                if (difference >= 64) { return 2; }
                return difference >= 40 ? 1 : 0;
            }
            if (extent <= 16)
            {
                if (difference >= 48) { return 2; }
                return difference >= 20 ? 1 : 0;
            }
            if (extent <= 24) { return difference >= 4 ? 3 : 0; }
            return 3;
        }

        private static bool UseUpsample(Av1TransformSize size, int delta, bool smooth)
        {
            int difference = Math.Abs(delta);
            if (difference == 0 || difference >= 40) { return false; }
            return size.Width + size.Height <= (smooth ? 8 : 16);
        }

        private static void FilterEdge(Span<byte> edge, int count, int strength)
        {
            if (strength == 0) { return; }
            ReadOnlySpan<byte> taps = strength switch
            {
                1 => [0, 4, 8, 4, 0],
                2 => [0, 5, 6, 5, 0],
                3 => [2, 4, 4, 4, 2],
                _ => throw new InvalidDataException("The AV1 intra edge strength is invalid.")
            };
            Span<byte> original = stackalloc byte[count];
            edge[..count].CopyTo(original);
            for (int sample = 1; sample < count; sample++)
            {
                int sum = 0;
                for (int tap = 0; tap < 5; tap++) { sum += taps[tap] * original[Math.Clamp(sample - 2 + tap, 0, count - 1)]; }
                edge[sample] = (byte)((sum + 8) >> 4);
            }
        }

        private static void Upsample(Span<byte> edge, int count)
        {
            Span<byte> original = stackalloc byte[count + 3];
            original[0] = edge[1];
            edge.Slice(1, count + 1).CopyTo(original[1..]);
            original[count + 2] = edge[count + 1];
            edge[0] = original[0];
            for (int sample = 0; sample < count; sample++)
            {
                int sum = -original[sample] + original[sample + 1] * 9 + original[sample + 2] * 9 - original[sample + 3];
                edge[sample * 2 + 1] = (byte)Math.Clamp((sum + 8) >> 4, 0, 255);
                edge[sample * 2 + 2] = original[sample + 2];
            }
        }
    }
}
