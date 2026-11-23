// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static class H264ChromaModeSearch
    {
        public static int Select(H264Picture target, int x, int y, ReadOnlySpan<byte> source, Span<byte> prediction)
        {
            return Select(target, x, y, source, prediction, out _);
        }

        public static int Select(H264Picture target, int x, int y, ReadOnlySpan<byte> source, Span<byte> prediction, out int best)
        {
            bool left = target.IsInCurrentSlice(x - 1, y);
            bool top = target.IsInCurrentSlice(x, y - 1);
            Span<byte> candidate = stackalloc byte[128];
            best = int.MaxValue;
            int selected = 0;
            for (int mode = 0; mode < 4; mode++)
            {
                if ((mode == 1 && !left) || (mode == 2 && !top) ||
                    (mode == 3 && !(left && top && target.IsInCurrentSlice(x - 1, y - 1))))
                {
                    continue;
                }
                target.PredictMacroblock(1, x / 2, y / 2, mode, candidate[..64]);
                target.PredictMacroblock(2, x / 2, y / 2, mode, candidate[64..]);
                Vector128<ushort> sum = Vector128<ushort>.Zero;
                for (int i = 0; i < candidate.Length; i += 16)
                {
                    sum += H264SampleDifference.Accumulate16(
                        Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)i),
                        Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(candidate), (nuint)i));
                }
                int difference = H264SampleDifference.Total(sum);
                if (difference < best)
                {
                    best = difference;
                    selected = mode;
                    candidate.CopyTo(prediction);
                    if (best == 0) { break; }
                }
            }
            return selected;
        }
    }
}
