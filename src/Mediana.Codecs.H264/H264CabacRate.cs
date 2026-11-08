// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264CabacRate
    {
        private static readonly double[] Logs = Enumerable.Range(0, 512).Select(i => i == 0 ? double.NegativeInfinity : Math.Log2(i)).ToArray();
        public static double Difference(H264CabacRateSample start, H264CabacRateSample end) =>
            (end.Bits - start.Bits) + Logs[start.Range] - Logs[end.Range];
        public static double LogRange(int range) => Logs[range];
    }
}
