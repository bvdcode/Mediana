// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264InverseTransform
    {
        private static ReadOnlySpan<int> Scan4x4 => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<int> ScaleEven => [10, 11, 13, 14, 16, 18];
        private static ReadOnlySpan<int> ScaleOdd => [16, 18, 20, 23, 25, 29];
        private static ReadOnlySpan<int> ScaleMixed => [13, 14, 16, 18, 20, 23];
        private static ReadOnlySpan<int> ChromaQuantizers =>
            [29, 30, 31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38, 39, 39, 39, 39];

        public static int ChromaQp(int lumaQp, int offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(lumaQp);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(lumaQp, 51);
            int index = Math.Clamp(lumaQp + offset, 0, 51);
            return index < 30 ? index : ChromaQuantizers[index - 30];
        }

        public static void Residual4x4(ReadOnlySpan<int> scanned, int qp, Span<int> residual, int? scaledDc = null)
        {
            ValidateQp(qp);
            if (scanned.Length != (scaledDc is null ? 16 : 15) || residual.Length != 16)
            {
                throw new ArgumentException("The H.264 residual transform requires a complete 4x4 block.");
            }
            Span<long> values = stackalloc long[16];
            if (scaledDc is not null)
            {
                values[0] = scaledDc.Value;
            }
            int scanOffset = scaledDc is null ? 0 : 1;
            for (int index = 0; index < scanned.Length; index++)
            {
                int position = Scan4x4[index + scanOffset];
                int row = position / 4;
                int column = position % 4;
                int scale = ((row & 1), (column & 1)) switch
                {
                    (0, 0) => ScaleEven[qp % 6],
                    (1, 1) => ScaleOdd[qp % 6],
                    (0, 1) or (1, 0) => ScaleMixed[qp % 6],
                    _ => throw new InvalidOperationException()
                };
                values[position] = ((long)scanned[index] * scale) << (qp / 6);
            }
            for (int row = 0; row < 4; row++)
            {
                TransformLine(values, row * 4, 1);
            }
            for (int column = 0; column < 4; column++)
            {
                TransformLine(values, column, 4);
            }
            for (int index = 0; index < 16; index++)
            {
                residual[index] = checked((int)((values[index] + 32) >> 6));
            }
        }

        public static void LumaDc(ReadOnlySpan<int> scanned, int qp, Span<int> rasterDc)
        {
            ValidateQp(qp);
            if (scanned.Length != 16 || rasterDc.Length != 16)
            {
                throw new ArgumentException("The H.264 luma DC transform requires 16 coefficients.");
            }
            Span<long> values = stackalloc long[16];
            for (int index = 0; index < 16; index++)
            {
                values[Scan4x4[index]] = scanned[index];
            }
            for (int row = 0; row < 4; row++)
            {
                HadamardLine(values, row * 4, 1);
            }
            for (int column = 0; column < 4; column++)
            {
                HadamardLine(values, column, 4);
            }
            int quotient = qp / 6;
            for (int index = 0; index < 16; index++)
            {
                long value = values[index] * ScaleEven[qp % 6];
                long scaled = quotient >= 2 ? value << (quotient - 2)
                    : (value + (1 << (1 - quotient))) >> (2 - quotient);
                rasterDc[index] = checked((int)scaled);
            }
        }

        public static void ChromaDc(ReadOnlySpan<int> scanned, int qp, Span<int> rasterDc)
        {
            ValidateQp(qp);
            if (scanned.Length != 4 || rasterDc.Length != 4)
            {
                throw new ArgumentException("The H.264 4:2:0 chroma DC transform requires four coefficients.");
            }
            long a = (long)scanned[0] + scanned[1];
            long b = (long)scanned[0] - scanned[1];
            long c = (long)scanned[2] + scanned[3];
            long d = (long)scanned[2] - scanned[3];
            Span<long> values = stackalloc long[] { a + c, b + d, a - c, b - d };
            for (int index = 0; index < 4; index++)
            {
                rasterDc[index] = checked((int)(((values[index] * ScaleEven[qp % 6]) << (qp / 6)) >> 1));
            }
        }

        private static void TransformLine(Span<long> values, int offset, int stride)
        {
            long a = values[offset] + values[offset + stride * 2];
            long b = values[offset] - values[offset + stride * 2];
            long c = (values[offset + stride] >> 1) - values[offset + stride * 3];
            long d = values[offset + stride] + (values[offset + stride * 3] >> 1);
            values[offset] = a + d;
            values[offset + stride] = b + c;
            values[offset + stride * 2] = b - c;
            values[offset + stride * 3] = a - d;
        }

        private static void HadamardLine(Span<long> values, int offset, int stride)
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

        private static void ValidateQp(int qp)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(qp);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(qp, 51);
        }
    }
}
