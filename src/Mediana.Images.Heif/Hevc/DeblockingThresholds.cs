// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal static class DeblockingThresholds
    {
        private static readonly byte[] BetaValues =
        {
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,6,7,8,9,10,11,12,13,14,15,
            16,17,18,20,22,24,26,28,30,32,34,36,38,40,42,44,46,48,50,52,54,56,58,60,62,64
        };
        private static readonly byte[] TcValues =
        {
            0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,
            2,2,2,2,3,3,3,3,4,4,4,5,5,6,6,7,8,9,10,11,13,14,16,18,20,22,24
        };

        public static int Beta(int qp, int depth) => BetaValues[Math.Min(51, Math.Max(0, qp))] << (depth - 8);
        public static int Tc(int qp, int depth) => TcValues[Math.Min(53, Math.Max(0, qp))] << (depth - 8);
    }
}
