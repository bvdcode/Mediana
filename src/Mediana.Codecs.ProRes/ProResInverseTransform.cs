// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.ProRes
{
    internal static class ProResInverseTransform
    {
        private static readonly double[] Basis = CreateBasis();
        private static ReadOnlySpan<byte> ProgressiveScan =>
        [
            0,1,4,5,16,17,21,22, 2,3,6,7,18,20,23,28,
            8,9,12,13,19,24,27,29, 10,11,14,15,25,26,30,31,
            32,33,37,38,45,46,53,54, 34,36,39,44,47,52,55,60,
            35,40,43,48,51,56,59,61, 41,42,49,50,57,58,62,63
        ];
        private static ReadOnlySpan<byte> InterlacedScan =>
        [
            0,2,8,10,32,34,35,41, 1,3,9,11,33,36,40,42,
            4,6,12,14,37,39,43,49, 5,7,13,15,38,44,48,50,
            16,18,19,25,45,47,51,57, 17,20,24,26,46,52,56,58,
            21,23,27,30,53,55,59,62, 22,28,29,31,54,60,61,63
        ];

        public static void Apply(ReadOnlySpan<int> scanned, int block, int blockCount,
            ReadOnlySpan<byte> weights, int scale, bool interlaced, int depth, Span<ushort> pixels)
        {
            ReadOnlySpan<byte> scan = interlaced ? InterlacedScan : ProgressiveScan;
            Span<double> coefficients = stackalloc double[64];
            bool hasAc = false;
            for (int frequency = 0; frequency < 64; frequency++)
            {
                int value = scanned[scan[frequency] * blockCount + block];
                coefficients[frequency] = value * (double)weights[frequency] * scale / 8;
                hasAc |= frequency != 0 && value != 0;
            }
            double sampleScale = (1 << depth) / 512.0;
            int maximum = (1 << depth) - 1;
            if (!hasAc)
            {
                ushort value = (ushort)Math.Clamp((int)Math.Floor((coefficients[0] / 8 + 256) * sampleScale + 0.5), 0, maximum);
                pixels.Fill(value);
                return;
            }
            Span<double> intermediate = stackalloc double[64];
            for (int row = 0; row < 8; row++)
            {
                ReadOnlySpan<double> source = coefficients.Slice(row * 8, 8);
                for (int x = 0; x < 8; x++)
                {
                    intermediate[x * 8 + row] = Dot(source, Basis.AsSpan(x * 8, 8));
                }
            }
            for (int y = 0; y < 8; y++)
            {
                ReadOnlySpan<double> basis = Basis.AsSpan(y * 8, 8);
                for (int x = 0; x < 8; x++)
                {
                    double sum = Dot(intermediate.Slice(x * 8, 8), basis);
                    pixels[y * 8 + x] = (ushort)Math.Clamp((int)Math.Floor((sum + 256) * sampleScale + 0.5), 0, maximum);
                }
            }
        }

        private static double Dot(ReadOnlySpan<double> values, ReadOnlySpan<double> basis)
        {
            if (Vector256.IsHardwareAccelerated)
            {
                ref double a = ref MemoryMarshal.GetReference(values);
                ref double b = ref MemoryMarshal.GetReference(basis);
                return Vector256.Dot(Vector256.LoadUnsafe(ref a), Vector256.LoadUnsafe(ref b)) +
                    Vector256.Dot(Vector256.LoadUnsafe(ref a, 4), Vector256.LoadUnsafe(ref b, 4));
            }
            return values[0] * basis[0] + values[1] * basis[1] + values[2] * basis[2] + values[3] * basis[3] +
                values[4] * basis[4] + values[5] * basis[5] + values[6] * basis[6] + values[7] * basis[7];
        }

        private static double[] CreateBasis()
        {
            double[] result = new double[64];
            for (int x = 0; x < 8; x++)
            {
                for (int frequency = 0; frequency < 8; frequency++)
                {
                    double normalization = frequency == 0 ? 1 / Math.Sqrt(2) : 1;
                    result[x * 8 + frequency] = normalization * Math.Cos((2 * x + 1) * frequency * Math.PI / 16) / 2;
                }
            }
            return result;
        }
    }
}
