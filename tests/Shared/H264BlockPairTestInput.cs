// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Tests
{
    internal static class H264BlockPairTestInput
    {
        private static ReadOnlySpan<int> Matrix => [1, 1, 1, 1, 2, 1, -1, -2, 1, -1, -1, 1, 1, -2, 2, -1];
        private static ReadOnlySpan<int> Scan => [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
        private static ReadOnlySpan<int> Factors =>
            [13107, 8066, 5243, 11916, 7490, 4660, 10082, 6554, 4194, 9362, 5825, 3647, 8192, 5243, 3355, 7282, 4559, 2893];

        public static void Verify()
        {
            byte[] source = new byte[192];
            byte[] prediction = new byte[192];
            int[] guarded = new int[64];
            Random random = new(9782341);
            int pairs = 0;
            foreach ((int sourceStride, int predictionStride) in new[] { (8, 8), (11, 17), (32, 16) })
            {
                for (int qp = 0; qp <= 51; qp++)
                {
                    for (int sample = 0; sample < 256; sample++)
                    {
                        random.NextBytes(source);
                        random.NextBytes(prediction);
                        VerifyPair(source, sourceStride, prediction, predictionStride, qp, guarded);
                        pairs++;
                    }
                }
            }
            foreach (int qp in new[] { 0, 26, 51 })
            {
                for (int pattern = 0; pattern < 65536; pattern++)
                {
                    for (int index = 0; index < 16; index++)
                    {
                        byte value = (byte)(((pattern >> index) & 1) * 255);
                        int position = index / 4 * 8 + index % 4;
                        source[3 + position] = value;
                        prediction[5 + position] = (byte)(255 - value);
                        source[3 + position + 4] = (byte)(255 - value);
                        prediction[5 + position + 4] = value;
                    }
                    VerifyPair(source, 8, prediction, 8, qp, guarded);
                    pairs++;
                }
            }
            H264ForwardTransform.QuantizePair(source.AsSpan(3), 8, prediction.AsSpan(5), 8, 26,
                guarded.AsSpan(2, 16), guarded.AsSpan(32, 16));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 1024; iteration++)
            {
                H264ForwardTransform.QuantizePair(source.AsSpan(3), 8, prediction.AsSpan(5), 8, 26,
                    guarded.AsSpan(2, 16), guarded.AsSpan(32, 16));
            }
            if (GC.GetAllocatedBytesForCurrentThread() != before)
            {
                throw new InvalidDataException("The paired residual kernel allocated heap storage.");
            }
            Console.WriteLine($"PASS {pairs} independent block pairs; all QPs, both scans, raw DC, guards, inputs and allocation; sse2={Sse2.IsSupported}");
        }

        private static void VerifyPair(byte[] source, int sourceStride, byte[] prediction, int predictionStride,
            int qp, int[] guarded)
        {
            byte[] sourceBefore = (byte[])source.Clone();
            byte[] predictionBefore = (byte[])prediction.Clone();
            Span<int> expected = stackalloc int[32];
            int firstDc = Expected(source.AsSpan(3), sourceStride, prediction.AsSpan(5), predictionStride, qp, expected[..16]);
            int secondDc = Expected(source.AsSpan(7), sourceStride, prediction.AsSpan(9), predictionStride, qp, expected[16..]);
            foreach (int size in new[] { 15, 16 })
            {
                Array.Fill(guarded, 7654321);
                (int actualFirstDc, int actualSecondDc) = H264ForwardTransform.QuantizePair(source.AsSpan(3), sourceStride,
                    prediction.AsSpan(5), predictionStride, qp, guarded.AsSpan(2, size), guarded.AsSpan(32, size));
                int skip = 16 - size;
                if (actualFirstDc != firstDc || actualSecondDc != secondDc ||
                    !guarded.AsSpan(2, size).SequenceEqual(expected.Slice(skip, size)) ||
                    !guarded.AsSpan(32, size).SequenceEqual(expected.Slice(16 + skip, size)))
                {
                    throw new InvalidDataException($"The paired transform or quantization differs from the matrix oracle at QP {qp}, scan {size}.");
                }
                for (int index = 0; index < guarded.Length; index++)
                {
                    if ((index < 2 || index >= 2 + size) && (index < 32 || index >= 32 + size) && guarded[index] != 7654321)
                    {
                        throw new InvalidDataException("The paired residual kernel changed an output guard.");
                    }
                }
            }
            if (!source.AsSpan().SequenceEqual(sourceBefore) || !prediction.AsSpan().SequenceEqual(predictionBefore))
            {
                throw new InvalidDataException("The paired residual kernel changed source samples.");
            }
        }

        private static int Expected(ReadOnlySpan<byte> source, int sourceStride, ReadOnlySpan<byte> prediction,
            int predictionStride, int qp, Span<int> scanned)
        {
            int dc = 0;
            int shift = 15 + qp / 6;
            long bias = (1L << shift) / 3;
            for (int index = 0; index < 16; index++)
            {
                int position = Scan[index];
                int row = position / 4;
                int column = position % 4;
                long coefficient = 0;
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        coefficient += (long)Matrix[row * 4 + y] * Matrix[column * 4 + x] *
                            (source[y * sourceStride + x] - prediction[y * predictionStride + x]);
                    }
                }
                if (position == 0)
                {
                    dc = checked((int)coefficient);
                }
                int scale = (row % 2, column % 2) switch
                {
                    (0, 0) => Factors[qp % 6 * 3],
                    (0, 1) or (1, 0) => Factors[qp % 6 * 3 + 1],
                    (1, 1) => Factors[qp % 6 * 3 + 2],
                    _ => throw new InvalidOperationException("The coefficient parity is invalid.")
                };
                long level = (Math.Abs(coefficient) * scale + bias) >> shift;
                scanned[index] = checked((int)(coefficient < 0 ? -level : level));
            }
            return dc;
        }
    }
}
