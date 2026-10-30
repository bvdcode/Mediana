// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264ForwardTransform
    {
        private static ReadOnlySpan<int> Scan => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<int> Even => [13107, 11916, 10082, 9362, 8192, 7282];
        private static ReadOnlySpan<int> Mixed => [8066, 7490, 6554, 5825, 5243, 4559];
        private static ReadOnlySpan<int> Odd => [5243, 4660, 4194, 3647, 3355, 2893];

        public static void Transform4x4(ReadOnlySpan<int> residual, Span<int> transformed)
        {
            if (residual.Length != 16 || transformed.Length != 16)
            {
                throw new ArgumentException("The forward H.264 transform requires 16 samples.");
            }
            residual.CopyTo(transformed);
            for (int row = 0; row < 4; row++)
            {
                TransformLine(transformed, row * 4, 1);
            }
            for (int column = 0; column < 4; column++)
            {
                TransformLine(transformed, column, 4);
            }
        }

        public static void Quantize4x4(ReadOnlySpan<int> transformed, int qp, Span<int> scanned)
        {
            if (transformed.Length != 16 || scanned.Length is not (15 or 16) || qp is < 0 or > 51)
            {
                throw new ArgumentException("The forward H.264 quantizer configuration is invalid.");
            }
            int skip = 16 - scanned.Length;
            for (int index = skip; index < 16; index++)
            {
                int position = Scan[index];
                int row = position / 4;
                int column = position % 4;
                int scale = ((row & 1), (column & 1)) switch
                {
                    (0, 0) => Even[qp % 6],
                    (1, 1) => Odd[qp % 6],
                    (0, 1) or (1, 0) => Mixed[qp % 6],
                    _ => throw new InvalidOperationException()
                };
                scanned[index - skip] = Quantize(transformed[position], scale, 15 + qp / 6);
            }
        }

        public static void ChromaDc(ReadOnlySpan<int> dc, int qp, Span<int> levels)
        {
            if (dc.Length != 4 || levels.Length != 4 || qp is < 0 or > 51)
            {
                throw new ArgumentException("The forward H.264 chroma DC transform requires four coefficients.");
            }
            int a = dc[0] + dc[1];
            int b = dc[0] - dc[1];
            int c = dc[2] + dc[3];
            int d = dc[2] - dc[3];
            int shift = 16 + qp / 6;
            int scale = Even[qp % 6];
            levels[0] = Quantize(a + c, scale, shift);
            levels[1] = Quantize(b + d, scale, shift);
            levels[2] = Quantize(a - c, scale, shift);
            levels[3] = Quantize(b - d, scale, shift);
        }

        private static int Quantize(int value, int scale, int shift)
        {
            long magnitude = ((long)Math.Abs(value) * scale + (1L << shift) / 3) >> shift;
            return checked((int)(value < 0 ? -magnitude : magnitude));
        }

        private static void TransformLine(Span<int> values, int offset, int stride)
        {
            int a = values[offset] + values[offset + stride * 3];
            int b = values[offset + stride] + values[offset + stride * 2];
            int c = values[offset + stride] - values[offset + stride * 2];
            int d = values[offset] - values[offset + stride * 3];
            values[offset] = a + b;
            values[offset + stride] = 2 * d + c;
            values[offset + stride * 2] = a - b;
            values[offset + stride * 3] = d - 2 * c;
        }
    }
}
