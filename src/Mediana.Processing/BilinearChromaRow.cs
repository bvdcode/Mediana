// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal readonly ref struct BilinearChromaRow<TSample> where TSample : unmanaged, IBinaryInteger<TSample>
    {
        private readonly ReadOnlySpan<TSample> top;
        private readonly ReadOnlySpan<TSample> bottom;
        private readonly double horizontalOffset;
        private readonly int horizontalSubsampling;
        private readonly double verticalFraction;

        public BilinearChromaRow(VideoFrame frame, int plane, int lumaY, ChromaLocation location)
        {
            VideoPlaneFormat format = frame.Format.PixelFormat.Planes[plane];
            int x = 0, y = 0;
            if (format.HorizontalSubsampling != 1 || format.VerticalSubsampling != 1)
            { (x, y) = ChromaSamplePosition.InHalfLumaSamples(location); }
            horizontalSubsampling = format.HorizontalSubsampling;
            horizontalOffset = horizontalSubsampling == 1 ? 0 : x / 2d;
            double verticalOffset = format.VerticalSubsampling == 1 ? 0 : y / 2d;
            int lastRow = frame.Planes[plane].Height - 1;
            double position = Math.Clamp((lumaY - verticalOffset) / format.VerticalSubsampling, 0, lastRow);
            int row = (int)position;
            verticalFraction = position - row;
            top = MemoryMarshal.Cast<byte, TSample>(frame.GetRow(plane, row));
            bottom = MemoryMarshal.Cast<byte, TSample>(frame.GetRow(plane, Math.Min(row + 1, lastRow)));
        }

        public double Sample(int lumaX)
        {
            double position = Math.Clamp((lumaX - horizontalOffset) / horizontalSubsampling, 0, top.Length - 1);
            int left = (int)position;
            int right = Math.Min(left + 1, top.Length - 1);
            double fraction = position - left;
            double topLeft = double.CreateChecked(top[left]), bottomLeft = double.CreateChecked(bottom[left]);
            double upper = topLeft + (double.CreateChecked(top[right]) - topLeft) * fraction;
            double lower = bottomLeft + (double.CreateChecked(bottom[right]) - bottomLeft) * fraction;
            return upper + (lower - upper) * verticalFraction;
        }

    }
}
