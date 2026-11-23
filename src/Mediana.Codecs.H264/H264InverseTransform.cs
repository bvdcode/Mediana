// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal static class H264InverseTransform
    {
        private static ReadOnlySpan<int> ScaleEven => [10, 11, 13, 14, 16, 18];
        private static ReadOnlySpan<int> ScaleOdd => [16, 18, 20, 23, 25, 29];
        private static ReadOnlySpan<int> ScaleMixed => [13, 14, 16, 18, 20, 23];

        public static int ChromaQp(int lumaQp, int offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(lumaQp);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(lumaQp, 51);
            return new H264Quantizer(8).ChromaValue(lumaQp, offset);
        }

        public static void Residual4x4(ReadOnlySpan<int> scanned, int qp, Span<int> residual, int? scaledDc = null,
            int bitDepth = 8, bool field = false)
        {
            ValidateQp(qp, bitDepth);
            if (scanned.Length != (scaledDc is null ? 16 : 15) || residual.Length != 16)
            {
                throw new ArgumentException("The H.264 residual transform requires a complete 4x4 block.");
            }
            ReadOnlySpan<int> ac = scaledDc is null ? scanned[1..] : scanned;
            if (!ac.ContainsAnyExcept(0))
            {
                long dc = scaledDc is int value ? value : ((long)scanned[0] * ScaleEven[qp % 6]) << (qp / 6);
                residual.Fill(checked((int)((dc + 32) >> 6)));
                return;
            }
            Span<long> values = stackalloc long[16];
            if (scaledDc is not null)
            {
                values[0] = scaledDc.Value;
            }
            int scanOffset = scaledDc is null ? 0 : 1;
            ReadOnlySpan<int> scan = H264CoefficientScans.FourByFour(field);
            int scaleIndex = qp % 6;
            int shift = qp / 6;
            int mixed = ScaleMixed[scaleIndex] << shift;
            ReadOnlySpan<int> scales = [ScaleEven[scaleIndex] << shift, mixed, mixed, ScaleOdd[scaleIndex] << shift];
            for (int index = 0; index < scanned.Length; index++)
            {
                int position = scan[index + scanOffset];
                int parity = (position & 1) | ((position >> 1) & 2);
                values[position] = (long)scanned[index] * scales[parity];
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

        public static void LumaDc(ReadOnlySpan<int> scanned, int qp, Span<int> rasterDc, int bitDepth = 8, bool field = false)
        {
            ValidateQp(qp, bitDepth);
            if (scanned.Length != 16 || rasterDc.Length != 16)
            {
                throw new ArgumentException("The H.264 luma DC transform requires 16 coefficients.");
            }
            Span<long> values = stackalloc long[16];
            ReadOnlySpan<int> scan = H264CoefficientScans.FourByFour(field);
            for (int index = 0; index < 16; index++)
            {
                values[scan[index]] = scanned[index];
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

        public static void ChromaDc(ReadOnlySpan<int> scanned, int qp, Span<int> rasterDc, int bitDepth = 8)
        {
            ValidateQp(qp, bitDepth);
            if (scanned.Length == 8 && rasterDc.Length == 8)
            {
                ChromaDc422(scanned, qp, rasterDc);
                return;
            }
            if (scanned.Length != 4 || rasterDc.Length != 4)
            {
                throw new ArgumentException("The H.264 chroma DC transform requires matching blocks of four or eight coefficients.");
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

        private static void ChromaDc422(ReadOnlySpan<int> scanned, int qp, Span<int> rasterDc)
        {
            ReadOnlySpan<int> scan = [0, 2, 1, 4, 6, 3, 5, 7];
            Span<long> values = stackalloc long[8];
            for (int index = 0; index < scanned.Length; index++)
            {
                values[scan[index]] = scanned[index];
            }
            for (int row = 0; row < 4; row++)
            {
                long left = values[row * 2];
                long right = values[row * 2 + 1];
                values[row * 2] = left + right;
                values[row * 2 + 1] = left - right;
            }
            HadamardLine(values, 0, 2);
            HadamardLine(values, 1, 2);
            int dcQp = qp + 3;
            int quotient = dcQp / 6;
            for (int index = 0; index < values.Length; index++)
            {
                long value = values[index] * ScaleEven[dcQp % 6];
                long scaled = quotient >= 2 ? value << (quotient - 2)
                    : (value + (1 << (1 - quotient))) >> (2 - quotient);
                rasterDc[index] = checked((int)scaled);
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

        internal static void ValidateQp(int qp, int bitDepth = 8)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(qp);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(qp, new H264Quantizer(bitDepth).Maximum);
        }
    }
}
