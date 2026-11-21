// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static partial class SrgbConverter
    {
        private static void ApplyPlanarAlpha(VideoFrame input, Span<byte> output, NearestNeighborMap map,
            int depth, CancellationToken token)
        {
            int maximum = (1 << depth) - 1;
            ReadOnlySpan<byte> alpha = default;
            int previousRow = -1;
            for (int y = 0; y < map.OutputHeight; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < map.OutputWidth; x++)
                {
                    if ((x & 1023) == 0) { token.ThrowIfCancellationRequested(); }
                    int sourceY = map.SourceY(x, y);
                    int sourceX = map.SourceX(x, y);
                    if (sourceY != previousRow)
                    {
                        alpha = input.GetRow(3, sourceY);
                        previousRow = sourceY;
                    }
                    int sample = depth > 8 ? MemoryMarshal.Cast<byte, ushort>(alpha)[sourceX] : alpha[sourceX];
                    output[(y * map.OutputWidth + x) * 4 + 3] = (byte)Math.Min(255, (sample * 255 + maximum / 2) / maximum);
                }
            }
        }
    }
}
