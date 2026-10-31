// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264BorderExtension
    {
        public static void Copy(ReadOnlySpan<byte> samples, int stride, int rows, long x, long y,
            int width, int height, Span<byte> output)
        {
            int left = (int)Math.Clamp(-x, 0, width);
            int start = (int)Math.Clamp(x, 0, stride);
            int count = (int)Math.Clamp((long)stride - Math.Max(x, 0), 0, width - left);
            for (int row = 0; row < height; row++)
            {
                ReadOnlySpan<byte> source = samples.Slice((int)Math.Clamp(y + row, 0, rows - 1) * stride, stride);
                Span<byte> destination = output.Slice(row * width, width);
                destination[..left].Fill(source[0]);
                source.Slice(start, count).CopyTo(destination[left..]);
                destination[(left + count)..].Fill(source[^1]);
            }
        }
    }
}
