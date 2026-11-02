// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.Intrinsics;
using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264SampleDifferenceTestInput
    {
        public static void Verify()
        {
            VerifyStridedBlocks();
            for (int left = 0; left <= 255; left++)
            {
                for (int right = 0; right <= 255; right++)
                {
                    int actual = H264SampleDifference.Sum16(Vector128.Create((byte)left), Vector128.Create((byte)right));
                    if (actual != 16 * Math.Abs(left - right))
                    {
                        throw new InvalidDataException("The vector difference does not match the scalar sum for a byte pair.");
                    }
                }
            }
            byte[] first = new byte[16];
            byte[] second = new byte[16];
            for (int pattern = 0; pattern < 4096; pattern++)
            {
                int expected = 0;
                for (int lane = 0; lane < 16; lane++)
                {
                    first[lane] = (byte)((pattern * 19 + lane * 53) & 255);
                    second[lane] = (byte)((pattern / 16 * 37 + lane * lane * 29) & 255);
                    expected += Math.Abs(first[lane] - second[lane]);
                }
                if (H264SampleDifference.Sum16(Vector128.LoadUnsafe(ref first[0]), Vector128.LoadUnsafe(ref second[0])) != expected)
                {
                    throw new InvalidDataException("The vector difference omitted or changed a mixed lane contribution.");
                }
            }
        }

        private static void VerifyStridedBlocks()
        {
            foreach (int stride in new[] { 16, 17, 32, 67 })
            {
                byte[] input = new byte[256 + 6];
                byte[] prediction = new byte[stride * 15 + 16 + 6];
                for (int pattern = 0; pattern < 32; pattern++)
                {
                    input.AsSpan().Fill(183);
                    prediction.AsSpan().Fill(183);
                    int[] prefixes = new int[16];
                    int sum = 0;
                    for (int row = 0; row < 16; row++)
                    {
                        for (int column = 0; column < 16; column++)
                        {
                            byte first = (byte)((pattern * 19 + row * 53 + column * 7) & 255);
                            byte second = (byte)((pattern * 37 + row * row * 29 + column * 11) & 255);
                            input[3 + row * 16 + column] = first;
                            prediction[3 + row * stride + column] = second;
                            sum += Math.Abs(first - second);
                        }
                        prefixes[row] = sum;
                    }
                    byte[] originalInput = input.ToArray();
                    byte[] originalPrediction = prediction.ToArray();
                    ReadOnlySpan<byte> source = input.AsSpan(3, 256);
                    ReadOnlySpan<byte> reference = prediction.AsSpan(3, stride * 15 + 16);
                    foreach (int prefix in prefixes)
                    {
                        foreach (int stop in new[] { 0, prefix - 1, prefix, prefix + 1, int.MaxValue })
                        {
                            int expected = prefixes[^1];
                            foreach (int partial in prefixes)
                            {
                                if (partial > stop)
                                {
                                    expected = partial;
                                    break;
                                }
                            }
                            if (H264SampleDifference.Sum16x16(source, reference, stride, stop) != expected)
                            {
                                throw new InvalidDataException("Strided motion differences changed a row rejection boundary or included padding.");
                            }
                        }
                    }
                    if (!input.AsSpan().SequenceEqual(originalInput) || !prediction.AsSpan().SequenceEqual(originalPrediction))
                    {
                        throw new InvalidDataException("Strided motion differences changed source samples or guards.");
                    }
                }
            }
        }
    }
}
