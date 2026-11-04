// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264BiPredictionVector
    {
        public static int Apply(ReadOnlySpan<byte> l0, ReadOnlySpan<byte> l1, Span<byte> output,
            in H264BiPredictionWeights weights)
        {
            if (!Sse2.IsSupported || output.Length < Vector128<byte>.Count)
            {
                return 0;
            }
            int length = output.Length - output.Length % Vector128<byte>.Count;
            if (weights.Offset == 0 && weights.Weight0 == 1 << weights.Denominator
                && weights.Weight1 == weights.Weight0)
            {
                for (int index = 0; index < length; index += Vector128<byte>.Count)
                {
                    Vector128<byte> first = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(l0), (nuint)index);
                    Vector128<byte> second = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(l1), (nuint)index);
                    H264SampleAverage.Rounded(first, second).StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)index);
                }
                return length;
            }
            short w0 = (short)weights.Weight0;
            short w1 = (short)weights.Weight1;
            Vector128<short> factors = Vector128.Create(w0, w1, w0, w1, w0, w1, w0, w1);
            Vector128<int> rounding = Vector128.Create(1 << weights.Denominator);
            Vector128<int> shift = Vector128.CreateScalar(weights.Denominator + 1);
            Vector128<int> offset = Vector128.Create(weights.Offset);
            for (int index = 0; index < length; index += Vector128<byte>.Count)
            {
                Vector128<byte> first = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(l0), (nuint)index);
                Vector128<byte> second = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(l1), (nuint)index);
                Vector128<short> firstLow = Sse2.UnpackLow(first, Vector128<byte>.Zero).AsInt16();
                Vector128<short> firstHigh = Sse2.UnpackHigh(first, Vector128<byte>.Zero).AsInt16();
                Vector128<short> secondLow = Sse2.UnpackLow(second, Vector128<byte>.Zero).AsInt16();
                Vector128<short> secondHigh = Sse2.UnpackHigh(second, Vector128<byte>.Zero).AsInt16();
                Vector128<short> lower = Sse2.PackSignedSaturate(
                    Combine(Sse2.UnpackLow(firstLow, secondLow), factors, rounding, shift, offset),
                    Combine(Sse2.UnpackHigh(firstLow, secondLow), factors, rounding, shift, offset));
                Vector128<short> upper = Sse2.PackSignedSaturate(
                    Combine(Sse2.UnpackLow(firstHigh, secondHigh), factors, rounding, shift, offset),
                    Combine(Sse2.UnpackHigh(firstHigh, secondHigh), factors, rounding, shift, offset));
                Sse2.PackUnsignedSaturate(lower, upper).StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)index);
            }
            return length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> Combine(Vector128<short> samples, Vector128<short> factors,
            Vector128<int> rounding, Vector128<int> shift, Vector128<int> offset) =>
            Sse2.ShiftRightArithmetic(Sse2.MultiplyAddAdjacent(samples, factors) + rounding, shift) + offset;
    }
}
