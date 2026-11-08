// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static partial class H264ForwardTransform
    {
        public static void LumaDc(ReadOnlySpan<int> rasterDc, int qp, Span<int> scanned)
        {
            H264InverseTransform.ValidateQp(qp);
            if (rasterDc.Length != 16 || scanned.Length != 16)
            {
                throw new ArgumentException("The forward H.264 luma DC transform requires 16 coefficients.");
            }
            Span<long> values = stackalloc long[16];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = rasterDc[index];
            }
            for (int row = 0; row < 4; row++)
            {
                ForwardHadamardLine(values, row * 4, 1);
            }
            for (int column = 0; column < 4; column++)
            {
                ForwardHadamardLine(values, column, 4);
            }
            int shift = 16 + qp / 6;
            long rounding = (1L << shift) / 3;
            int scale = Even[qp % 6];
            for (int index = 0; index < 16; index++)
            {
                int coefficient = checked((int)((values[Scan[index]] + 1) >> 1));
                scanned[index] = Quantize(coefficient, scale, shift, rounding);
            }
        }

        private static void ForwardHadamardLine(Span<long> values, int offset, int stride)
        {
            long a = values[offset] + values[offset + stride * 3];
            long b = values[offset + stride] + values[offset + stride * 2];
            long c = values[offset + stride] - values[offset + stride * 2];
            long d = values[offset] - values[offset + stride * 3];
            values[offset] = a + b;
            values[offset + stride] = d + c;
            values[offset + stride * 2] = a - b;
            values[offset + stride * 3] = d - c;
        }
    }
}
