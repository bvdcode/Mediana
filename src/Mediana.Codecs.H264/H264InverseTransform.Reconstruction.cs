// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static partial class H264InverseTransform
    {
        // Two inverse butterflies amplify a coefficient by at most (7/2)^2 before rounding.
        private const int MaximumVectorInput = 1 << 27;

        internal static bool TryReconstructFrame4x4(ReadOnlySpan<int> scanned, int qp, int? scaledDc,
            ReadOnlySpan<byte> prediction, int predictionStride, Span<byte> pixels, int stride)
        {
            ReadOnlySpan<int> ac = scaledDc.HasValue ? scanned : scanned[1..];
            int index = qp % 6;
            int shift = qp / 6;
            int even = ScaleEven[index] << shift;
            long dc = scaledDc ?? (long)scanned[0] * even;
            Vector128<byte> result;
            if (!ac.ContainsAnyExcept(0))
            {
                int delta = checked((int)((dc + 32) >> 6));
                Vector128<byte> predicted = LoadRows(prediction, predictionStride);
                result = delta >= 0 ? Vector128.AddSaturate(predicted, Vector128.Create((byte)Math.Min(255, delta)))
                    : Vector128.SubtractSaturate(predicted, Vector128.Create((byte)-Math.Max(-255, delta)));
            }
            else
            {
                // Even the largest 8-bit scale (29 << 8) keeps these levels inside the butterfly bound.
                if (dc is < -MaximumVectorInput or > MaximumVectorInput || ac.ContainsAnyExceptInRange(-16384, 16384))
                {
                    return false;
                }
                int mixed = ScaleMixed[index] << shift;
                int odd = ScaleOdd[index] << shift;
                Vector128<int> evenColumn = Vector128.Create(even, mixed, even, mixed);
                Vector128<int> oddColumn = Vector128.Create(mixed, odd, mixed, odd);
                Vector128<int> a = (Vector128.Create(0, ac[1], ac[2], ac[8]) * evenColumn).WithElement(0, (int)dc);
                Vector128<int> b = Vector128.Create(ac[0], ac[3], ac[7], ac[9]) * oddColumn;
                Vector128<int> c = Vector128.Create(ac[4], ac[6], ac[10], ac[13]) * evenColumn;
                Vector128<int> d = Vector128.Create(ac[5], ac[11], ac[12], ac[14]) * oddColumn;
                InverseButterfly(ref a, ref b, ref c, ref d);
                H264TransformMatrix.Transpose(ref a, ref b, ref c, ref d);
                InverseButterfly(ref a, ref b, ref c, ref d);
                Vector128<int> rounding = Vector128.Create(32);
                Vector128<short> top = Vector128.NarrowWithSaturation((a + rounding) >> 6, (b + rounding) >> 6);
                Vector128<short> bottom = Vector128.NarrowWithSaturation((c + rounding) >> 6, (d + rounding) >> 6);
                Vector128<byte> predicted = LoadRows(prediction, predictionStride);
                top = Vector128.AddSaturate(top, Vector128.WidenLower(predicted).AsInt16());
                bottom = Vector128.AddSaturate(bottom, Vector128.WidenUpper(predicted).AsInt16());
                result = Vector128.NarrowWithSaturation(Vector128.Max(top, Vector128<short>.Zero).AsUInt16(),
                    Vector128.Max(bottom, Vector128<short>.Zero).AsUInt16());
            }
            Vector128<uint> rows = result.AsUInt32();
            for (int row = 0; row < 4; row++)
            {
                uint samples = rows.GetElement(row);
                MemoryMarshal.Write(pixels[(row * stride)..], in samples);
            }
            return true;
        }

        private static Vector128<byte> LoadRows(ReadOnlySpan<byte> source, int stride) =>
            Vector128.Create(MemoryMarshal.Read<uint>(source), MemoryMarshal.Read<uint>(source[stride..]),
                MemoryMarshal.Read<uint>(source[(2 * stride)..]), MemoryMarshal.Read<uint>(source[(3 * stride)..])).AsByte();

        private static void InverseButterfly(ref Vector128<int> first, ref Vector128<int> second,
            ref Vector128<int> third, ref Vector128<int> fourth)
        {
            Vector128<int> sum = first + third;
            Vector128<int> difference = first - third;
            Vector128<int> oddDifference = (second >> 1) - fourth;
            Vector128<int> oddSum = second + (fourth >> 1);
            first = sum + oddSum;
            second = difference + oddDifference;
            third = difference - oddDifference;
            fourth = sum - oddSum;
        }
    }
}
