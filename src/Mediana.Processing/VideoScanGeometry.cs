// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Processing
{
    internal static class VideoScanGeometry
    {
        public static VideoScanMode Map(VideoScanMode mode, NearestNeighborMap map)
        {
            switch (mode)
            {
                case VideoScanMode.Progressive:
                    return mode;
                case VideoScanMode.TopFieldFirst:
                case VideoScanMode.BottomFieldFirst:
                case VideoScanMode.TopField:
                case VideoScanMode.BottomField:
                    if (map.Height == map.SourceHeight && !map.Orientation.SwapsAxes && map.Orientation.D == 1)
                    {
                        return mode;
                    }
                    throw new NotSupportedException("Changing field row geometry requires deinterlacing before this operation.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public static void RequireProgressive(VideoFormat format)
        {
            switch (format.ScanMode)
            {
                case VideoScanMode.Progressive:
                    return;
                case VideoScanMode.TopFieldFirst:
                case VideoScanMode.BottomFieldFirst:
                case VideoScanMode.TopField:
                case VideoScanMode.BottomField:
                    throw new NotSupportedException("This operation requires progressive samples. Deinterlace the source first.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }
    }
}
