// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264VectorInverse8x8
    {
        private const int MaximumScale = 58;
        private const int MaximumScaledLevel = (int.MaxValue - 128) / 64;

        private static ReadOnlySpan<byte> ScaleClasses => [0, 3, 4, 3, 3, 1, 5, 1, 4, 5, 2, 5, 3, 1, 5, 1];
        private static readonly byte[] Scan = PrepareScan();
        private static readonly byte[] Factors = PrepareFactors();

        internal static bool IsSupported => Vector128.IsHardwareAccelerated;

        internal static bool TryWrite(ReadOnlySpan<int> coefficients, int qp,
            ReadOnlySpan<byte> prediction, int predictionStride, Span<byte> destination, int stride)
        {
            if (!IsSupported)
            {
                return false;
            }
            int shift = qp / 6 - 2;
            int limit = MaximumScaledLevel / (MaximumScale << Math.Max(shift, 0));
            if (coefficients.ContainsAnyExceptInRange(-limit, limit))
            {
                return false;
            }

            ReadOnlySpan<byte> factors = Factors.AsSpan((qp % 6) * 64, 64);
            ReadOnlySpan<byte> scan = Scan;
            Span<int> values = stackalloc int[64];
            if (shift >= 0)
            {
                for (int i = 0; i < 64; i++)
                {
                    values[scan[i]] = coefficients[i] * factors[i] << shift;
                }
            }
            else
            {
                int right = -shift;
                int rounding = 1 << (right - 1);
                for (int i = 0; i < 64; i++)
                {
                    values[scan[i]] = (coefficients[i] * factors[i] + rounding) >> right;
                }
            }

            if (!Avx2.IsSupported)
            {
                WritePortable(values, prediction, predictionStride, destination, stride);
                return true;
            }
            ref int input = ref MemoryMarshal.GetReference(values);
            Vector256<int> a = Vector256.LoadUnsafe(ref input);
            Vector256<int> b = Vector256.LoadUnsafe(ref input, 8);
            Vector256<int> c = Vector256.LoadUnsafe(ref input, 16);
            Vector256<int> d = Vector256.LoadUnsafe(ref input, 24);
            Vector256<int> e = Vector256.LoadUnsafe(ref input, 32);
            Vector256<int> f = Vector256.LoadUnsafe(ref input, 40);
            Vector256<int> g = Vector256.LoadUnsafe(ref input, 48);
            Vector256<int> h = Vector256.LoadUnsafe(ref input, 56);
            Inverse(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            Transpose(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            Inverse(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            WriteRow(a, prediction, destination);
            WriteRow(b, prediction[predictionStride..], destination[stride..]);
            WriteRow(c, prediction[(predictionStride * 2)..], destination[(stride * 2)..]);
            WriteRow(d, prediction[(predictionStride * 3)..], destination[(stride * 3)..]);
            WriteRow(e, prediction[(predictionStride * 4)..], destination[(stride * 4)..]);
            WriteRow(f, prediction[(predictionStride * 5)..], destination[(stride * 5)..]);
            WriteRow(g, prediction[(predictionStride * 6)..], destination[(stride * 6)..]);
            WriteRow(h, prediction[(predictionStride * 7)..], destination[(stride * 7)..]);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteRow(Vector256<int> transformed, ReadOnlySpan<byte> prediction, Span<byte> destination)
        {
            Vector256<int> scaled = (transformed + Vector256.Create(32)) >> 6;
            H264SampleReconstruction.Write8Row(prediction, scaled.GetLower(), scaled.GetUpper(), destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Transpose(ref Vector256<int> a, ref Vector256<int> b, ref Vector256<int> c, ref Vector256<int> d,
            ref Vector256<int> e, ref Vector256<int> f, ref Vector256<int> g, ref Vector256<int> h)
        {
            Vector256<long> ab0 = Avx2.UnpackLow(a, b).AsInt64();
            Vector256<long> ab1 = Avx2.UnpackHigh(a, b).AsInt64();
            Vector256<long> cd0 = Avx2.UnpackLow(c, d).AsInt64();
            Vector256<long> cd1 = Avx2.UnpackHigh(c, d).AsInt64();
            Vector256<long> ef0 = Avx2.UnpackLow(e, f).AsInt64();
            Vector256<long> ef1 = Avx2.UnpackHigh(e, f).AsInt64();
            Vector256<long> gh0 = Avx2.UnpackLow(g, h).AsInt64();
            Vector256<long> gh1 = Avx2.UnpackHigh(g, h).AsInt64();
            Vector256<int> top0 = Avx2.UnpackLow(ab0, cd0).AsInt32();
            Vector256<int> top1 = Avx2.UnpackHigh(ab0, cd0).AsInt32();
            Vector256<int> top2 = Avx2.UnpackLow(ab1, cd1).AsInt32();
            Vector256<int> top3 = Avx2.UnpackHigh(ab1, cd1).AsInt32();
            Vector256<int> bottom0 = Avx2.UnpackLow(ef0, gh0).AsInt32();
            Vector256<int> bottom1 = Avx2.UnpackHigh(ef0, gh0).AsInt32();
            Vector256<int> bottom2 = Avx2.UnpackLow(ef1, gh1).AsInt32();
            Vector256<int> bottom3 = Avx2.UnpackHigh(ef1, gh1).AsInt32();
            a = Avx2.Permute2x128(top0, bottom0, 0x20);
            b = Avx2.Permute2x128(top1, bottom1, 0x20);
            c = Avx2.Permute2x128(top2, bottom2, 0x20);
            d = Avx2.Permute2x128(top3, bottom3, 0x20);
            e = Avx2.Permute2x128(top0, bottom0, 0x31);
            f = Avx2.Permute2x128(top1, bottom1, 0x31);
            g = Avx2.Permute2x128(top2, bottom2, 0x31);
            h = Avx2.Permute2x128(top3, bottom3, 0x31);
        }

        private static void WritePortable(Span<int> values, ReadOnlySpan<byte> prediction, int predictionStride,
            Span<byte> destination, int stride)
        {
            ref int start = ref MemoryMarshal.GetReference(values);
            for (nuint group = 0; group < 8; group += 4)
            {
                Vector128<int> a = Vector128.LoadUnsafe(ref start, group);
                Vector128<int> b = Vector128.LoadUnsafe(ref start, group + 8);
                Vector128<int> c = Vector128.LoadUnsafe(ref start, group + 16);
                Vector128<int> d = Vector128.LoadUnsafe(ref start, group + 24);
                Vector128<int> e = Vector128.LoadUnsafe(ref start, group + 32);
                Vector128<int> f = Vector128.LoadUnsafe(ref start, group + 40);
                Vector128<int> g = Vector128.LoadUnsafe(ref start, group + 48);
                Vector128<int> h = Vector128.LoadUnsafe(ref start, group + 56);
                Inverse(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
                H264TransformMatrix.Transpose(ref a, ref b, ref c, ref d);
                H264TransformMatrix.Transpose(ref e, ref f, ref g, ref h);
                a.StoreUnsafe(ref start, group);
                e.StoreUnsafe(ref start, group + 8);
                b.StoreUnsafe(ref start, group + 16);
                f.StoreUnsafe(ref start, group + 24);
                c.StoreUnsafe(ref start, group + 32);
                g.StoreUnsafe(ref start, group + 40);
                d.StoreUnsafe(ref start, group + 48);
                h.StoreUnsafe(ref start, group + 56);
            }
            for (int part = 0; part < 2; part++)
            {
                nuint offset = (nuint)(part * 8);
                Vector128<int> a = Vector128.LoadUnsafe(ref start, offset);
                Vector128<int> b = Vector128.LoadUnsafe(ref start, offset + 16);
                Vector128<int> c = Vector128.LoadUnsafe(ref start, offset + 32);
                Vector128<int> d = Vector128.LoadUnsafe(ref start, offset + 48);
                Vector128<int> e = Vector128.LoadUnsafe(ref start, offset + 4);
                Vector128<int> f = Vector128.LoadUnsafe(ref start, offset + 20);
                Vector128<int> g = Vector128.LoadUnsafe(ref start, offset + 36);
                Vector128<int> h = Vector128.LoadUnsafe(ref start, offset + 52);
                Inverse(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
                int x = part * 4;
                WriteRow(a, prediction[x..], destination[x..]);
                WriteRow(b, prediction[(predictionStride + x)..], destination[(stride + x)..]);
                WriteRow(c, prediction[(predictionStride * 2 + x)..], destination[(stride * 2 + x)..]);
                WriteRow(d, prediction[(predictionStride * 3 + x)..], destination[(stride * 3 + x)..]);
                WriteRow(e, prediction[(predictionStride * 4 + x)..], destination[(stride * 4 + x)..]);
                WriteRow(f, prediction[(predictionStride * 5 + x)..], destination[(stride * 5 + x)..]);
                WriteRow(g, prediction[(predictionStride * 6 + x)..], destination[(stride * 6 + x)..]);
                WriteRow(h, prediction[(predictionStride * 7 + x)..], destination[(stride * 7 + x)..]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteRow(Vector128<int> transformed, ReadOnlySpan<byte> prediction, Span<byte> destination) =>
            H264SampleReconstruction.Write4Row(prediction, (transformed + Vector128.Create(32)) >> 6, destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Inverse(ref Vector128<int> d0, ref Vector128<int> d1, ref Vector128<int> d2, ref Vector128<int> d3,
            ref Vector128<int> d4, ref Vector128<int> d5, ref Vector128<int> d6, ref Vector128<int> d7)
        {
            Vector128<int> e0 = d0 + d4;
            Vector128<int> e1 = -d3 + d5 - d7 - (d7 >> 1);
            Vector128<int> e2 = d0 - d4;
            Vector128<int> e3 = d1 + d7 - d3 - (d3 >> 1);
            Vector128<int> e4 = (d2 >> 1) - d6;
            Vector128<int> e5 = -d1 + d7 + d5 + (d5 >> 1);
            Vector128<int> e6 = d2 + (d6 >> 1);
            Vector128<int> e7 = d3 + d5 + d1 + (d1 >> 1);
            Vector128<int> f0 = e0 + e6;
            Vector128<int> f1 = e1 + (e7 >> 2);
            Vector128<int> f2 = e2 + e4;
            Vector128<int> f3 = e3 + (e5 >> 2);
            Vector128<int> f4 = e2 - e4;
            Vector128<int> f5 = (e3 >> 2) - e5;
            Vector128<int> f6 = e0 - e6;
            Vector128<int> f7 = e7 - (e1 >> 2);
            d0 = f0 + f7;
            d1 = f2 + f5;
            d2 = f4 + f3;
            d3 = f6 + f1;
            d4 = f6 - f1;
            d5 = f4 - f3;
            d6 = f2 - f5;
            d7 = f0 - f7;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Inverse(ref Vector256<int> d0, ref Vector256<int> d1, ref Vector256<int> d2, ref Vector256<int> d3,
            ref Vector256<int> d4, ref Vector256<int> d5, ref Vector256<int> d6, ref Vector256<int> d7)
        {
            Vector256<int> e0 = d0 + d4;
            Vector256<int> e1 = -d3 + d5 - d7 - (d7 >> 1);
            Vector256<int> e2 = d0 - d4;
            Vector256<int> e3 = d1 + d7 - d3 - (d3 >> 1);
            Vector256<int> e4 = (d2 >> 1) - d6;
            Vector256<int> e5 = -d1 + d7 + d5 + (d5 >> 1);
            Vector256<int> e6 = d2 + (d6 >> 1);
            Vector256<int> e7 = d3 + d5 + d1 + (d1 >> 1);
            Vector256<int> f0 = e0 + e6;
            Vector256<int> f1 = e1 + (e7 >> 2);
            Vector256<int> f2 = e2 + e4;
            Vector256<int> f3 = e3 + (e5 >> 2);
            Vector256<int> f4 = e2 - e4;
            Vector256<int> f5 = (e3 >> 2) - e5;
            Vector256<int> f6 = e0 - e6;
            Vector256<int> f7 = e7 - (e1 >> 2);
            d0 = f0 + f7;
            d1 = f2 + f5;
            d2 = f4 + f3;
            d3 = f6 + f1;
            d4 = f6 - f1;
            d5 = f4 - f3;
            d6 = f2 - f5;
            d7 = f0 - f7;
        }

        private static byte[] PrepareScan()
        {
            ReadOnlySpan<int> scan = H264CoefficientScans.EightByEight(false);
            byte[] result = new byte[64];
            for (int i = 0; i < result.Length; i++)
            {
                int position = scan[i];
                result[i] = (byte)((position & 7) * 8 + position / 8);
            }
            return result;
        }

        private static byte[] PrepareFactors()
        {
            ReadOnlySpan<int> scan = H264CoefficientScans.EightByEight(false);
            ReadOnlySpan<byte> scales = H264InverseTransform8x8.Scales;
            ReadOnlySpan<byte> classes = ScaleClasses;
            byte[] factors = new byte[6 * 64];
            for (int qp = 0; qp < 6; qp++)
            {
                for (int i = 0; i < 64; i++)
                {
                    int position = scan[i];
                    int category = classes[((position >> 1) & 12) | (position & 3)];
                    factors[qp * 64 + i] = scales[qp * 6 + category];
                }
            }
            return factors;
        }
    }
}
