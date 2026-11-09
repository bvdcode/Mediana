// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1DirectionalPrediction
    {
        private static void PrepareEdges<T>(Av1TransformSize size, Av1IntraEdges<T> edges, int angle, bool enabled, int bitDepth, out int above, out int left) where T : unmanaged, IBinaryInteger<T>
        {
            above = left = 0;
            if (!enabled || angle is 90 or 180) { return; }
            if (angle is > 90 and < 180 && size.Width + size.Height >= 24)
            {
                int corner = int.CreateTruncating(edges.Left[2]) * 5 + int.CreateTruncating(edges.Above[1]) * 6 +
                    int.CreateTruncating(edges.Above[2]) * 5;
                edges.Left[1] = edges.Above[1] = T.CreateTruncating((corner + 8) >> 4);
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
                Upsample(edges.Above, size.Width + (angle < 90 ? size.Height : 0), bitDepth);
            }
            if (UseUpsample(size, angle - 180, edges.SmoothNeighbors))
            {
                left = 1;
                Upsample(edges.Left, size.Height + (angle > 180 ? size.Width : 0), bitDepth);
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

        private static void FilterEdge<T>(Span<T> edge, int count, int strength) where T : unmanaged, IBinaryInteger<T>
        {
            if (strength == 0) { return; }
            ReadOnlySpan<byte> taps = strength switch
            {
                1 => [0, 4, 8, 4, 0],
                2 => [0, 5, 6, 5, 0],
                3 => [2, 4, 4, 4, 2],
                _ => throw new InvalidDataException("The AV1 intra edge strength is invalid.")
            };
            Span<T> original = stackalloc T[count];
            edge[..count].CopyTo(original);
            for (int sample = 1; sample < count; sample++)
            {
                int sum = 0;
                for (int tap = 0; tap < 5; tap++) { sum += taps[tap] * int.CreateTruncating(original[Math.Clamp(sample - 2 + tap, 0, count - 1)]); }
                edge[sample] = T.CreateTruncating((sum + 8) >> 4);
            }
        }

        private static void Upsample<T>(Span<T> edge, int count, int bitDepth) where T : unmanaged, IBinaryInteger<T>
        {
            int maximum = (1 << bitDepth) - 1;
            Span<T> original = stackalloc T[count + 3];
            original[0] = edge[1];
            edge.Slice(1, count + 1).CopyTo(original[1..]);
            original[count + 2] = edge[count + 1];
            edge[0] = original[0];
            for (int sample = 0; sample < count; sample++)
            {
                int sum = -int.CreateTruncating(original[sample]) + int.CreateTruncating(original[sample + 1]) * 9 +
                    int.CreateTruncating(original[sample + 2]) * 9 - int.CreateTruncating(original[sample + 3]);
                edge[sample * 2 + 1] = T.CreateTruncating(Math.Clamp((sum + 8) >> 4, 0, maximum));
                edge[sample * 2 + 2] = original[sample + 2];
            }
        }
    }
}
