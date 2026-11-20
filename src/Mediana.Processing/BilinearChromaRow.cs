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

        public BilinearChromaRow(VideoFrame frame, int plane, int lumaY, ChromaLocation location,
            VideoFieldChromaLocations? fieldLocations = null)
        {
            VideoPlaneFormat format = frame.Format.PixelFormat.Planes[plane];
            (VideoFieldParity? parity, int step, int offset) = frame.Format.ScanMode switch
            {
                VideoScanMode.Progressive => ((VideoFieldParity?)null, 1, 0),
                VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst =>
                    ((lumaY & 1) == 0 ? VideoFieldParity.Top : VideoFieldParity.Bottom, 2, lumaY & 1),
                VideoScanMode.TopField => (VideoFieldParity.Top, 1, 0),
                VideoScanMode.BottomField => (VideoFieldParity.Bottom, 1, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
            if (parity is VideoFieldParity fieldParity && fieldLocations is VideoFieldChromaLocations locations)
            {
                location = locations.GetLocation(fieldParity);
            }
            int x = 0, y = 0;
            if (format.HorizontalSubsampling != 1 || format.VerticalSubsampling != 1)
            { (x, y) = ChromaSamplePosition.InHalfLumaSamples(location); }
            horizontalSubsampling = format.HorizontalSubsampling;
            horizontalOffset = horizontalSubsampling == 1 ? 0 : x / 2d;
            double verticalOffset = format.VerticalSubsampling == 1 ? 0 : y / 2d;
            if (parity is VideoFieldParity selected && format.VerticalSubsampling != 1)
            {
                verticalOffset = ChromaSamplePosition.InQuarterFieldLumaSamples(location, selected).Vertical / 4d;
            }
            int lastRow = (frame.Planes[plane].Height + step - 1 - offset) / step - 1;
            double position = Math.Clamp((lumaY / step - verticalOffset) / format.VerticalSubsampling, 0, lastRow);
            int row = (int)position;
            verticalFraction = position - row;
            top = MemoryMarshal.Cast<byte, TSample>(frame.GetRow(plane, row * step + offset));
            bottom = MemoryMarshal.Cast<byte, TSample>(frame.GetRow(plane, Math.Min(row + 1, lastRow) * step + offset));
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
