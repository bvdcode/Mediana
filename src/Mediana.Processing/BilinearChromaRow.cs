// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly ref struct BilinearChromaRow
    {
        private readonly ReadOnlySpan<byte> top;
        private readonly ReadOnlySpan<byte> bottom;
        private readonly double horizontalOffset;
        private readonly double verticalFraction;

        public BilinearChromaRow(VideoFrame frame, int plane, int lumaY, ChromaLocation location)
        {
            (int x, int y) = ChromaSamplePosition.InHalfLumaSamples(location);
            horizontalOffset = x / 2d;
            int lastRow = frame.Planes[plane].Height - 1;
            double position = Math.Clamp((lumaY - y / 2d) / 2, 0, lastRow);
            int row = (int)position;
            verticalFraction = position - row;
            top = frame.GetRow(plane, row);
            bottom = frame.GetRow(plane, Math.Min(row + 1, lastRow));
        }

        public double Sample(int lumaX)
        {
            double position = Math.Clamp((lumaX - horizontalOffset) / 2, 0, top.Length - 1);
            int left = (int)position;
            int right = Math.Min(left + 1, top.Length - 1);
            double fraction = position - left;
            double upper = top[left] + (top[right] - top[left]) * fraction;
            double lower = bottom[left] + (bottom[right] - bottom[left]) * fraction;
            return upper + (lower - upper) * verticalFraction;
        }

    }
}
