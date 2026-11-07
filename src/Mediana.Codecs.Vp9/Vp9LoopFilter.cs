// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9LoopFilter
    {
        internal static void Apply<T>(Span<T> samples, int offset, int step, int size, int level, int limit, int depth)
            where T : unmanaged, IBinaryInteger<T>
        {
            int p0 = int.CreateTruncating(samples[offset - step]), p1 = int.CreateTruncating(samples[offset - 2 * step]);
            int p2 = int.CreateTruncating(samples[offset - 3 * step]), p3 = int.CreateTruncating(samples[offset - 4 * step]);
            int q0 = int.CreateTruncating(samples[offset]), q1 = int.CreateTruncating(samples[offset + step]);
            int q2 = int.CreateTruncating(samples[offset + 2 * step]), q3 = int.CreateTruncating(samples[offset + 3 * step]);
            int scale = 1 << (depth - 8), boundary = (2 * (level + 2) + limit) * scale; limit *= scale;
            if (Math.Abs(p3 - p2) > limit || Math.Abs(p2 - p1) > limit || Math.Abs(p1 - p0) > limit ||
                Math.Abs(q1 - q0) > limit || Math.Abs(q2 - q1) > limit || Math.Abs(q3 - q2) > limit ||
                Math.Abs(p0 - q0) * 2 + Math.Abs(p1 - q1) / 2 > boundary) { return; }
            bool flat = size > 0 && Math.Abs(p1 - p0) <= scale && Math.Abs(p2 - p0) <= scale && Math.Abs(p3 - p0) <= scale &&
                Math.Abs(q1 - q0) <= scale && Math.Abs(q2 - q0) <= scale && Math.Abs(q3 - q0) <= scale;
            if (flat)
            {
                bool wide = size == 2;
                if (wide)
                {
                    for (int i = 4; i < 8; i++)
                    {
                        if (Math.Abs(int.CreateTruncating(samples[offset - (i + 1) * step]) - p0) > scale ||
                            Math.Abs(int.CreateTruncating(samples[offset + i * step]) - q0) > scale) { wide = false; break; }
                    }
                }
                Wide(samples, offset, step, wide ? 4 : 3); return;
            }
            int middle = 1 << (depth - 1), threshold = (level >> 4) * scale;
            bool hev = Math.Abs(p1 - p0) > threshold || Math.Abs(q1 - q0) > threshold;
            int filter = hev ? Math.Clamp(p1 - q1, -middle, middle - 1) : 0;
            filter = Math.Clamp(filter + 3 * (q0 - p0), -middle, middle - 1);
            int first = Math.Clamp(filter + 4, -middle, middle - 1) >> 3, second = Math.Clamp(filter + 3, -middle, middle - 1) >> 3;
            samples[offset] = T.CreateTruncating(Math.Clamp(q0 - first, 0, 2 * middle - 1));
            samples[offset - step] = T.CreateTruncating(Math.Clamp(p0 + second, 0, 2 * middle - 1));
            if (!hev)
            {
                int outer = (first + 1) >> 1;
                samples[offset + step] = T.CreateTruncating(Math.Clamp(q1 - outer, 0, 2 * middle - 1));
                samples[offset - 2 * step] = T.CreateTruncating(Math.Clamp(p1 + outer, 0, 2 * middle - 1));
            }
        }

        private static void Wide<T>(Span<T> samples, int offset, int step, int logSize) where T : unmanaged, IBinaryInteger<T>
        {
            Span<int> input = stackalloc int[16], prefix = stackalloc int[17];
            int count = 1 << logSize, half = count / 2; prefix[0] = 0;
            for (int i = 0; i < count; i++) { input[i] = int.CreateTruncating(samples[offset + (i - half) * step]); prefix[i + 1] = prefix[i] + input[i]; }
            for (int i = 1; i < count - 1; i++)
            {
                int low = i - (half - 1), high = i + (half - 1), start = Math.Max(0, low), end = Math.Min(count - 1, high);
                int sum = input[i] + prefix[end + 1] - prefix[start] + (start - low) * input[0] + (high - end) * input[count - 1];
                samples[offset + (i - half) * step] = T.CreateTruncating((sum + half) >> logSize);
            }
        }
    }
}
