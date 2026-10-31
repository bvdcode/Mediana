// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264BorderExtension
    {
        public static void Copy(ReadOnlySpan<byte> samples, int stride, int rows, long x, long y,
            int width, int height, Span<byte> output) =>
            Copy(samples, stride, stride, rows, x, y, width, height, output);

        public static void Copy(VideoFrame input, int plane, int x, int y, int width, int height, Span<byte> output)
        {
            PlaneLayout layout = input.Planes[plane];
            ReadOnlySpan<byte> samples = input.Data.Span.Slice(layout.Offset, checked((int)(layout.EndOffset - layout.Offset)));
            Copy(samples, layout.Stride, layout.RowBytes, layout.Height, x, y, width, height, output);
        }

        private static void Copy(ReadOnlySpan<byte> samples, int stride, int rowBytes, int rows, long x, long y,
            int width, int height, Span<byte> output)
        {
            if (x >= 0 && x <= rowBytes - width && y >= 0 && y <= rows - height)
            {
                for (int row = 0; row < height; row++)
                {
                    samples.Slice(((int)y + row) * stride + (int)x, width).CopyTo(output.Slice(row * width, width));
                }
                return;
            }
            int left = (int)Math.Clamp(-x, 0, width);
            int start = (int)Math.Clamp(x, 0, rowBytes);
            int count = (int)Math.Clamp((long)rowBytes - Math.Max(x, 0), 0, width - left);
            for (int row = 0; row < height; row++)
            {
                ReadOnlySpan<byte> source = samples.Slice((int)Math.Clamp(y + row, 0, rows - 1) * stride, rowBytes);
                Span<byte> destination = output.Slice(row * width, width);
                destination[..left].Fill(source[0]);
                source.Slice(start, count).CopyTo(destination[left..]);
                destination[(left + count)..].Fill(source[^1]);
            }
        }
    }
}
