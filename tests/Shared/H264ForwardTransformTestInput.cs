// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264ForwardTransformTestInput
    {
        private static ReadOnlySpan<int> Matrix => [1, 1, 1, 1, 2, 1, -1, -2, 1, -1, -1, 1, 1, -2, 2, -1];

        public static void Verify()
        {
            int[] residual = new int[16];
            foreach (int value in new[] { int.MinValue, -255, -1, 0, 1, 255, int.MaxValue })
            {
                Array.Fill(residual, value);
                VerifyBlock(residual);
                for (int position = 0; position < residual.Length; position++)
                {
                    Array.Clear(residual);
                    residual[position] = value;
                    VerifyBlock(residual);
                }
                for (int position = 0; position < residual.Length; position++)
                {
                    residual[position] = position % 2 == 0 ? value : unchecked(-value);
                }
                VerifyBlock(residual);
            }
            Random random = new(741);
            for (int sample = 0; sample < 4096; sample++)
            {
                for (int position = 0; position < residual.Length; position++)
                {
                    residual[position] = sample % 2 == 0
                        ? random.Next(-255, 256)
                        : (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1);
                }
                VerifyBlock(residual);
            }
        }

        public static void VerifyAllocation()
        {
            int[] residual = Enumerable.Range(-8, 16).ToArray();
            int[] transformed = new int[16];
            H264ForwardTransform.Transform4x4(residual, transformed);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 1024; iteration++)
            {
                H264ForwardTransform.Transform4x4(residual, transformed);
            }
            if (GC.GetAllocatedBytesForCurrentThread() != before)
            {
                throw new InvalidDataException("The forward block transform allocated heap storage.");
            }
        }

        private static void VerifyBlock(ReadOnlySpan<int> residual)
        {
            int[] expected = new int[16];
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    long sum = 0;
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            sum += (long)Matrix[row * 4 + y] * residual[y * 4 + x] * Matrix[column * 4 + x];
                        }
                    }
                    expected[row * 4 + column] = unchecked((int)sum);
                }
            }
            foreach (int destination in new[] { 2, 3, 4, 21 })
            {
                int[] storage = new int[40];
                Array.Fill(storage, 8675309);
                residual.CopyTo(storage.AsSpan(3, 16));
                int[] expectedStorage = (int[])storage.Clone();
                expected.CopyTo(expectedStorage, destination);
                H264ForwardTransform.Transform4x4(storage.AsSpan(3, 16), storage.AsSpan(destination, 16));
                if (!storage.AsSpan().SequenceEqual(expectedStorage))
                {
                    throw new InvalidDataException("The forward block transform differs from matrix multiplication or changed surrounding storage.");
                }
            }
        }
    }
}
