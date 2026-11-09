// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    public static partial class SrgbConverter
    {
        private static void RenderGbr<T, TTransform>(VideoFrame input, Span<byte> output, NearestNeighborMap map, int depth,
            TTransform transform, CancellationToken cancellationToken)
            where T : unmanaged, IBinaryInteger<T> where TTransform : struct, IRgbPixelTransform
        {
            double maximum = (1 << depth) - 1;
            ReadOnlySpan<T> red = default, green = default, blue = default; int cachedRow = -1;
            for (int y = 0; y < map.OutputHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<byte> row = output.Slice(y * map.OutputWidth * 4, map.OutputWidth * 4);
                for (int x = 0; x < map.OutputWidth; x++)
                {
                    int sourceY = map.SourceY(x, y), sourceX = map.SourceX(x, y);
                    if (sourceY != cachedRow)
                    {
                        green = MemoryMarshal.Cast<byte, T>(input.GetRow(0, sourceY)); blue = MemoryMarshal.Cast<byte, T>(input.GetRow(1, sourceY));
                        red = MemoryMarshal.Cast<byte, T>(input.GetRow(2, sourceY)); cachedRow = sourceY;
                    }
                    transform.Write(row.Slice(x * 4, 4), double.CreateChecked(red[sourceX]) / maximum,
                        double.CreateChecked(green[sourceX]) / maximum, double.CreateChecked(blue[sourceX]) / maximum, 255);
                }
            }
        }
    }
}
