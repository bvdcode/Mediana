// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    internal static class QuantizationMath
    {
        private static readonly byte[] ChromaQp = { 29, 30, 31, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37 };

        public static int Chroma(int qp, int chromaFormat = 1)
        {
            switch (chromaFormat)
            {
                case 1:
                    break;
                case 2:
                case 3:
                    return Math.Min(qp, 51);
                default:
                    throw new ArgumentOutOfRangeException(nameof(chromaFormat));
            }
            if ((qp >= 30) && (qp <= 43))
            {
                return ChromaQp[qp - 30];
            }
            return qp > 43 ? qp - 6 : qp;
        }
    }
}
