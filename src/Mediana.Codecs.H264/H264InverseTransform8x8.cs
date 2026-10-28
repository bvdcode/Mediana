namespace Mediana.Codecs.H264
{
    internal static class H264InverseTransform8x8
    {
        // Rec. ITU-T H.264, Table 8-14 and clause 8.5.9 (flat scaling lists).
        private static ReadOnlySpan<byte> Scan =>
            [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
                12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
                35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
                58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63];
        private static ReadOnlySpan<byte> Scales =>
            [20, 18, 32, 19, 25, 24, 22, 19, 35, 21, 28, 26,
                26, 23, 42, 24, 33, 31, 28, 25, 45, 26, 35, 33,
                32, 28, 51, 30, 40, 38, 36, 32, 58, 34, 46, 43];

        public static void Residual(ReadOnlySpan<int> scanned, int qp, Span<int> residual)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(qp);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(qp, 51);
            if (scanned.Length != 64 || residual.Length != 64)
            {
                throw new ArgumentException("The H.264 residual transform requires a complete 8x8 block.");
            }
            Span<long> values = stackalloc long[64];
            int quotient = qp / 6;
            for (int index = 0; index < 64; index++)
            {
                int position = Scan[index];
                int row = position / 8;
                int column = position % 8;
                int category = ((row % 4), (column % 4)) switch
                {
                    (0, 0) => 0,
                    (1 or 3, 1 or 3) => 1,
                    (2, 2) => 2,
                    (0, 1 or 3) or (1 or 3, 0) => 3,
                    (0, 2) or (2, 0) => 4,
                    (2, 1 or 3) or (1 or 3, 2) => 5,
                    _ => throw new InvalidOperationException("Invalid H.264 8x8 scaling position.")
                };
                long value = (long)scanned[index] * Scales[qp % 6 * 6 + category];
                values[position] = quotient >= 2 ? value << (quotient - 2)
                    : (value + (1 << (1 - quotient))) >> (2 - quotient);
            }
            for (int row = 0; row < 8; row++)
            {
                TransformLine(values, row * 8, 1);
            }
            for (int column = 0; column < 8; column++)
            {
                TransformLine(values, column, 8);
            }
            for (int index = 0; index < 64; index++)
            {
                residual[index] = checked((int)((values[index] + 32) >> 6));
            }
        }

        private static void TransformLine(Span<long> values, int offset, int stride)
        {
            long d0 = values[offset];
            long d1 = values[offset + stride];
            long d2 = values[offset + stride * 2];
            long d3 = values[offset + stride * 3];
            long d4 = values[offset + stride * 4];
            long d5 = values[offset + stride * 5];
            long d6 = values[offset + stride * 6];
            long d7 = values[offset + stride * 7];
            long e0 = d0 + d4;
            long e1 = -d3 + d5 - d7 - (d7 >> 1);
            long e2 = d0 - d4;
            long e3 = d1 + d7 - d3 - (d3 >> 1);
            long e4 = (d2 >> 1) - d6;
            long e5 = -d1 + d7 + d5 + (d5 >> 1);
            long e6 = d2 + (d6 >> 1);
            long e7 = d3 + d5 + d1 + (d1 >> 1);
            long f0 = e0 + e6;
            long f1 = e1 + (e7 >> 2);
            long f2 = e2 + e4;
            long f3 = e3 + (e5 >> 2);
            long f4 = e2 - e4;
            long f5 = (e3 >> 2) - e5;
            long f6 = e0 - e6;
            long f7 = e7 - (e1 >> 2);
            values[offset] = f0 + f7;
            values[offset + stride] = f2 + f5;
            values[offset + stride * 2] = f4 + f3;
            values[offset + stride * 3] = f6 + f1;
            values[offset + stride * 4] = f6 - f1;
            values[offset + stride * 5] = f4 - f3;
            values[offset + stride * 6] = f2 - f5;
            values[offset + stride * 7] = f0 - f7;
        }
    }
}
