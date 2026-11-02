// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static partial class H264ForwardTransform
    {
        public static (int FirstDc, int SecondDc) QuantizePair(ReadOnlySpan<byte> source, int sourceStride,
            ReadOnlySpan<byte> prediction, int predictionStride, int qp, Span<int> first, Span<int> second)
        {
            if (qp is < 0 or > 51 || first.Length is not (15 or 16) || second.Length != first.Length ||
                sourceStride < 8 || predictionStride < 8 || source.Length < 3L * sourceStride + 8 ||
                prediction.Length < 3L * predictionStride + 8)
            {
                throw new ArgumentException("The forward H.264 block pair configuration is invalid.");
            }
            if (!Sse2.IsSupported)
            {
                Span<int> transformed = stackalloc int[16];
                H264EncodingBlocks.Transform(source, sourceStride, prediction, predictionStride, transformed);
                int firstDc = transformed[0];
                Quantize4x4(transformed, qp, first);
                H264EncodingBlocks.Transform(source[4..], sourceStride, prediction[4..], predictionStride, transformed);
                int secondDc = transformed[0];
                Quantize4x4(transformed, qp, second);
                return (firstDc, secondDc);
            }
            Vector128<short> row0 = ReadPairRow(source, 0, prediction, 0);
            Vector128<short> row1 = ReadPairRow(source, sourceStride, prediction, predictionStride);
            Vector128<short> row2 = ReadPairRow(source, sourceStride * 2, prediction, predictionStride * 2);
            Vector128<short> row3 = ReadPairRow(source, sourceStride * 3, prediction, predictionStride * 3);
            TransformPairVectors(ref row0, ref row1, ref row2, ref row3);
            TransposePair(ref row0, ref row1, ref row2, ref row3);
            TransformPairVectors(ref row0, ref row1, ref row2, ref row3);
            (int, int) dc = (row0.GetElement(0), row0.GetElement(4));
            int remainder = qp % 6;
            Vector128<ushort> even = Vector128.Create((ushort)Even[remainder], (ushort)Mixed[remainder],
                (ushort)Even[remainder], (ushort)Mixed[remainder], (ushort)Even[remainder], (ushort)Mixed[remainder],
                (ushort)Even[remainder], (ushort)Mixed[remainder]);
            Vector128<ushort> odd = Vector128.Create((ushort)Mixed[remainder], (ushort)Odd[remainder],
                (ushort)Mixed[remainder], (ushort)Odd[remainder], (ushort)Mixed[remainder], (ushort)Odd[remainder],
                (ushort)Mixed[remainder], (ushort)Odd[remainder]);
            int shift = 15 + qp / 6;
            Vector128<uint> rounding = Vector128.Create((uint)((1 << shift) / 3));
            Vector128<uint> count = Vector128.CreateScalar((uint)shift);
            row0 = QuantizePairColumn(row0, even, rounding, count);
            row1 = QuantizePairColumn(row1, odd, rounding, count);
            row2 = QuantizePairColumn(row2, even, rounding, count);
            row3 = QuantizePairColumn(row3, odd, rounding, count);
            WritePairScan(Vector128.WidenLower(row0), Vector128.WidenLower(row1),
                Vector128.WidenLower(row2), Vector128.WidenLower(row3), first);
            WritePairScan(Vector128.WidenUpper(row0), Vector128.WidenUpper(row1),
                Vector128.WidenUpper(row2), Vector128.WidenUpper(row3), second);
            return dc;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<short> ReadPairRow(ReadOnlySpan<byte> source, int sourceOffset,
            ReadOnlySpan<byte> prediction, int predictionOffset) =>
            (Vector128.WidenLower(H264VectorSamples.Read8(source, sourceOffset)) -
                Vector128.WidenLower(H264VectorSamples.Read8(prediction, predictionOffset))).AsInt16();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TransformPairVectors(ref Vector128<short> first, ref Vector128<short> second,
            ref Vector128<short> third, ref Vector128<short> fourth)
        {
            Vector128<short> outerSum = first + fourth;
            Vector128<short> innerSum = second + third;
            Vector128<short> innerDifference = second - third;
            Vector128<short> outerDifference = first - fourth;
            first = outerSum + innerSum;
            second = Vector128.ShiftLeft(outerDifference, 1) + innerDifference;
            third = outerSum - innerSum;
            fourth = outerDifference - Vector128.ShiftLeft(innerDifference, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TransposePair(ref Vector128<short> first, ref Vector128<short> second,
            ref Vector128<short> third, ref Vector128<short> fourth)
        {
            Vector128<short> a = Sse2.UnpackLow(first, second);
            Vector128<short> b = Sse2.UnpackLow(third, fourth);
            Vector128<short> c = Sse2.UnpackHigh(first, second);
            Vector128<short> d = Sse2.UnpackHigh(third, fourth);
            Vector128<long> lowA = Sse2.UnpackLow(a.AsInt32(), b.AsInt32()).AsInt64();
            Vector128<long> highA = Sse2.UnpackHigh(a.AsInt32(), b.AsInt32()).AsInt64();
            Vector128<long> lowB = Sse2.UnpackLow(c.AsInt32(), d.AsInt32()).AsInt64();
            Vector128<long> highB = Sse2.UnpackHigh(c.AsInt32(), d.AsInt32()).AsInt64();
            first = Sse2.UnpackLow(lowA, lowB).AsInt16();
            second = Sse2.UnpackHigh(lowA, lowB).AsInt16();
            third = Sse2.UnpackLow(highA, highB).AsInt16();
            fourth = Sse2.UnpackHigh(highA, highB).AsInt16();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<short> QuantizePairColumn(Vector128<short> values, Vector128<ushort> scales,
            Vector128<uint> rounding, Vector128<uint> shift)
        {
            Vector128<short> sign = Vector128.ShiftRightArithmetic(values, 15);
            Vector128<ushort> magnitude = ((values ^ sign) - sign).AsUInt16();
            Vector128<ushort> low = Sse2.MultiplyLow(magnitude, scales);
            Vector128<ushort> high = Sse2.MultiplyHigh(magnitude, scales);
            Vector128<uint> lower = Sse2.ShiftRightLogical(Sse2.UnpackLow(low, high).AsUInt32() + rounding, shift);
            Vector128<uint> upper = Sse2.ShiftRightLogical(Sse2.UnpackHigh(low, high).AsUInt32() + rounding, shift);
            Vector128<short> levels = Sse2.PackSignedSaturate(lower.AsInt32(), upper.AsInt32());
            return (levels ^ sign) - sign;
        }

        private static void WritePairScan(Vector128<int> first, Vector128<int> second,
            Vector128<int> third, Vector128<int> fourth, Span<int> output)
        {
            Transpose(ref first, ref second, ref third, ref fourth);
            WriteQuantizedScan(first, second, third, fourth, output);
        }
    }
}
