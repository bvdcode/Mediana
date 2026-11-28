// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static partial class H264VectorInverse8x8
    {
        private static readonly (ulong X, ulong Y) OddFrequencies = GetOddFrequencyMasks();

        private static (ulong X, ulong Y) GetOddFrequencyMasks()
        {
            ulong x = 0;
            ulong y = 0;
            ReadOnlySpan<int> scan = H264CoefficientScans.EightByEight(false);
            for (int index = 0; index < scan.Length; index++)
            {
                if ((scan[index] & 1) != 0)
                {
                    x |= 1UL << index;
                }
                if ((scan[index] & 8) != 0)
                {
                    y |= 1UL << index;
                }
            }
            return (x, y);
        }

        // At QP 41 the largest signed CABAC level scales to 32768 * 58 * 16.
        private const int PreparedQuantizerLimit = 41;
        internal const int PreparedIndexBits = 6;
        internal const int PreparedIndexMask = (1 << PreparedIndexBits) - 1;

        internal static bool SupportsPrepared(int qp) => IsSupported && (uint)qp <= PreparedQuantizerLimit;

        internal static H264ResidualSymmetry GetSymmetry(ulong positions)
        {
            H264ResidualSymmetry horizontal = (positions & OddFrequencies.X) == 0
                ? H264ResidualSymmetry.Horizontal : H264ResidualSymmetry.None;
            H264ResidualSymmetry vertical = (positions & OddFrequencies.Y) == 0
                ? H264ResidualSymmetry.Vertical : H264ResidualSymmetry.None;
            return horizontal | vertical;
        }

        internal static int PreparedLength(H264ResidualSymmetry symmetry) => symmetry switch
        {
            H264ResidualSymmetry.None => 64,
            H264ResidualSymmetry.Horizontal or H264ResidualSymmetry.Vertical => 32,
            H264ResidualSymmetry.Both => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(symmetry)),
        };

        internal static ReadOnlySpan<int> PreparedLayout(int qp, H264ResidualSymmetry symmetry) =>
            PreparedLayouts.AsSpan(((int)symmetry * (PreparedQuantizerLimit + 1) + qp) * 64, 64);

        private static int[] CreatePreparedLayouts()
        {
            const int quantizers = PreparedQuantizerLimit + 1;
            int[] layouts = new int[4 * quantizers * 64];
            for (int symmetry = 0; symmetry < 4; symmetry++)
            {
                int horizontalShift = symmetry & 1;
                int verticalShift = symmetry >> 1;
                for (int qp = 0; qp < quantizers; qp++)
                {
                    int offset = (symmetry * quantizers + qp) * 64;
                    int shift = Math.Max(qp / 6 - 2, 0);
                    for (int index = 0; index < 64; index++)
                    {
                        int frequencyX = Scan[index] >> 3;
                        int frequencyY = Scan[index] & 7;
                        int slot = ((frequencyX >> horizontalShift) << (3 - verticalShift)) + (frequencyY >> verticalShift);
                        layouts[offset + index] = (Factors[qp % 6 * 64 + index] << (shift + PreparedIndexBits)) | slot;
                    }
                }
            }
            return layouts;
        }

        internal static void WritePrepared(Span<int> values, H264ResidualSymmetry symmetry, ReadOnlySpan<byte> prediction,
            int predictionStride, Span<byte> destination, int stride)
        {
            ref int start = ref MemoryMarshal.GetReference(values);
            switch (symmetry)
            {
                case H264ResidualSymmetry.None:
                    WriteTransposed(values, prediction, predictionStride, destination, stride);
                    break;
                case H264ResidualSymmetry.Horizontal:
                    WriteMirroredX(ref start, prediction, predictionStride, destination, stride);
                    break;
                case H264ResidualSymmetry.Vertical:
                    WriteMirroredY(ref start, prediction, predictionStride, destination, stride);
                    break;
                case H264ResidualSymmetry.Both:
                    WriteMirroredBoth(ref start, prediction, predictionStride, destination, stride);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(symmetry));
            }
        }

        private static void WriteMirroredX(ref int start, ReadOnlySpan<byte> prediction,
            int predictionStride, Span<byte> destination, int stride)
        {
            InverseEvenBand(ref start, 0, 8, out Vector128<int> a, out Vector128<int> b, out Vector128<int> c, out Vector128<int> d);
            InverseEvenBand(ref start, 4, 8, out Vector128<int> e, out Vector128<int> f, out Vector128<int> g, out Vector128<int> h);
            Inverse(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            WriteMirrorRow(a, prediction, destination);
            WriteMirrorRow(b, prediction[predictionStride..], destination[stride..]);
            WriteMirrorRow(c, prediction[(2 * predictionStride)..], destination[(2 * stride)..]);
            WriteMirrorRow(d, prediction[(3 * predictionStride)..], destination[(3 * stride)..]);
            WriteMirrorRow(e, prediction[(4 * predictionStride)..], destination[(4 * stride)..]);
            WriteMirrorRow(f, prediction[(5 * predictionStride)..], destination[(5 * stride)..]);
            WriteMirrorRow(g, prediction[(6 * predictionStride)..], destination[(6 * stride)..]);
            WriteMirrorRow(h, prediction[(7 * predictionStride)..], destination[(7 * stride)..]);
        }

        private static void WriteMirroredY(ref int start, ReadOnlySpan<byte> prediction,
            int predictionStride, Span<byte> destination, int stride)
        {
            Vector128<int> a = Vector128.LoadUnsafe(ref start, 0);
            Vector128<int> b = Vector128.LoadUnsafe(ref start, 4);
            Vector128<int> c = Vector128.LoadUnsafe(ref start, 8);
            Vector128<int> d = Vector128.LoadUnsafe(ref start, 12);
            Vector128<int> e = Vector128.LoadUnsafe(ref start, 16);
            Vector128<int> f = Vector128.LoadUnsafe(ref start, 20);
            Vector128<int> g = Vector128.LoadUnsafe(ref start, 24);
            Vector128<int> h = Vector128.LoadUnsafe(ref start, 28);
            Inverse(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h);
            H264TransformMatrix.Transpose(ref a, ref b, ref c, ref d);
            H264TransformMatrix.Transpose(ref e, ref f, ref g, ref h);
            InverseEven(ref a, ref b, ref c, ref d);
            InverseEven(ref e, ref f, ref g, ref h);
            WriteDoubleRow(a, e, 0, prediction, predictionStride, destination, stride);
            WriteDoubleRow(b, f, 1, prediction, predictionStride, destination, stride);
            WriteDoubleRow(c, g, 2, prediction, predictionStride, destination, stride);
            WriteDoubleRow(d, h, 3, prediction, predictionStride, destination, stride);
        }

        private static void WriteMirroredBoth(ref int start, ReadOnlySpan<byte> prediction,
            int predictionStride, Span<byte> destination, int stride)
        {
            InverseEvenBand(ref start, 0, 4, out Vector128<int> a, out Vector128<int> b, out Vector128<int> c, out Vector128<int> d);
            InverseEven(ref a, ref b, ref c, ref d);
            WriteDoubleRow(a, ReverseLanes(a), 0, prediction, predictionStride, destination, stride);
            WriteDoubleRow(b, ReverseLanes(b), 1, prediction, predictionStride, destination, stride);
            WriteDoubleRow(c, ReverseLanes(c), 2, prediction, predictionStride, destination, stride);
            WriteDoubleRow(d, ReverseLanes(d), 3, prediction, predictionStride, destination, stride);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InverseEvenBand(ref int start, nuint offset, nuint stride,
            out Vector128<int> a, out Vector128<int> b, out Vector128<int> c, out Vector128<int> d)
        {
            a = Vector128.LoadUnsafe(ref start, offset);
            b = Vector128.LoadUnsafe(ref start, offset + stride);
            c = Vector128.LoadUnsafe(ref start, offset + 2 * stride);
            d = Vector128.LoadUnsafe(ref start, offset + 3 * stride);
            InverseEven(ref a, ref b, ref c, ref d);
            H264TransformMatrix.Transpose(ref a, ref b, ref c, ref d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InverseEven(ref Vector128<int> dc, ref Vector128<int> second,
            ref Vector128<int> fourth, ref Vector128<int> sixth)
        {
            // Without odd frequencies the outer output pairs coincide.
            Vector128<int> sum = dc + fourth;
            Vector128<int> difference = dc - fourth;
            Vector128<int> wide = second + (sixth >> 1);
            Vector128<int> narrow = (second >> 1) - sixth;
            dc = sum + wide;
            second = difference + narrow;
            fourth = difference - narrow;
            sixth = sum - wide;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> ReverseLanes(Vector128<int> values) =>
            Vector128.Shuffle(values, Vector128.Create(3, 2, 1, 0));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteMirrorRow(Vector128<int> values, ReadOnlySpan<byte> prediction, Span<byte> destination)
        {
            Vector128<int> rounded = (values + Vector128.Create(32)) >> 6;
            H264SampleReconstruction.Write8Row(prediction, rounded, ReverseLanes(rounded), destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteDoubleRow(Vector128<int> left, Vector128<int> right, int row,
            ReadOnlySpan<byte> prediction, int predictionStride, Span<byte> destination, int stride)
        {
            Vector128<int> rounding = Vector128.Create(32);
            left = (left + rounding) >> 6;
            right = (right + rounding) >> 6;
            H264SampleReconstruction.Write8Row(prediction[(row * predictionStride)..], left, right, destination[(row * stride)..]);
            int mirror = 7 - row;
            H264SampleReconstruction.Write8Row(prediction[(mirror * predictionStride)..], left, right, destination[(mirror * stride)..]);
        }
    }
}
