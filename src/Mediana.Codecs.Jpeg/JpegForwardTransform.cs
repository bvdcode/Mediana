// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegForwardTransform
    {
        private const float C0 = 0.35355339059f;
        private const float C1 = 0.49039264020f;
        private const float C2 = 0.46193976626f;
        private const float C3 = 0.41573480615f;
        private const float C4 = 0.35355339059f;
        private const float C5 = 0.27778511651f;
        private const float C6 = 0.19134171618f;
        private const float C7 = 0.09754516101f;

        internal static void Quantize(ReadOnlySpan<float> samples, ReadOnlySpan<float> reciprocals, Span<int> coefficients)
        {
            Span<float> vertical = stackalloc float[64];
            Span<float> transposed = stackalloc float[64];
            Transform(samples, vertical);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++) { transposed[x * 8 + y] = vertical[y * 8 + x]; }
            }
            Transform(transposed, vertical);
            Span<int> quantized = stackalloc int[64];
            for (int u = 0; u < 8; u++)
            {
                Vector256<float> scaled = Vector256.LoadUnsafe(ref vertical[u * 8])
                    * Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(reciprocals), (nuint)(u * 8));
                Vector256<float> rounded = Vector256.CopySign(Vector256.Floor(Vector256.Abs(scaled) + Vector256.Create(0.5f)), scaled);
                Vector256.ConvertToInt32(rounded).StoreUnsafe(ref quantized[u * 8]);
            }
            for (int v = 0; v < 8; v++)
            {
                for (int u = 0; u < 8; u++) { coefficients[v * 8 + u] = quantized[u * 8 + v]; }
            }
        }

        private static void Transform(ReadOnlySpan<float> input, Span<float> output)
        {
            ref float first = ref MemoryMarshal.GetReference(input);
            Vector256<float> x0 = Vector256.LoadUnsafe(ref first);
            Vector256<float> x1 = Vector256.LoadUnsafe(ref first, 8);
            Vector256<float> x2 = Vector256.LoadUnsafe(ref first, 16);
            Vector256<float> x3 = Vector256.LoadUnsafe(ref first, 24);
            Vector256<float> x4 = Vector256.LoadUnsafe(ref first, 32);
            Vector256<float> x5 = Vector256.LoadUnsafe(ref first, 40);
            Vector256<float> x6 = Vector256.LoadUnsafe(ref first, 48);
            Vector256<float> x7 = Vector256.LoadUnsafe(ref first, 56);
            Vector256<float> s0 = x0 + x7, s1 = x1 + x6, s2 = x2 + x5, s3 = x3 + x4;
            Vector256<float> d0 = x0 - x7, d1 = x1 - x6, d2 = x2 - x5, d3 = x3 - x4;
            Vector256<float> a = s0 + s3, b = s1 + s2, c = s0 - s3, d = s1 - s2;
            ((a + b) * C0).StoreUnsafe(ref output[0]);
            ((a - b) * C4).StoreUnsafe(ref output[32]);
            (c * C2 + d * C6).StoreUnsafe(ref output[16]);
            (c * C6 - d * C2).StoreUnsafe(ref output[48]);
            (d0 * C1 + d1 * C3 + d2 * C5 + d3 * C7).StoreUnsafe(ref output[8]);
            (d0 * C3 - d1 * C7 - d2 * C1 - d3 * C5).StoreUnsafe(ref output[24]);
            (d0 * C5 - d1 * C1 + d2 * C7 + d3 * C3).StoreUnsafe(ref output[40]);
            (d0 * C7 - d1 * C5 + d2 * C3 - d3 * C1).StoreUnsafe(ref output[56]);
        }
    }
}
