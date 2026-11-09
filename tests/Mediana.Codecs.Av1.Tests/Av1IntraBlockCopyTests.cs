// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1.Tests
{
    [Collection("Allocation")]
    public class Av1IntraBlockCopyTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(12)]
        public void MatchesSeparableBilinearConvolutionWithoutAllocationOrExteriorChanges(int depth)
        {
            switch (depth)
            {
                case 8:
                    Verify<byte>(depth);
                    break;
                case 10 or 12:
                    Verify<ushort>(depth);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(depth));
            }
        }

        private static void Verify<T>(int depth) where T : unmanaged, IBinaryInteger<T>
        {
            Random random = new(1735 + depth);
            for (int subX = 0; subX <= 1; subX++)
            {
                for (int subY = 0; subY <= 1; subY++)
                {
                    int stride = 512 >> subX;
                    int height = 192 >> subY;
                    int x = 400 >> subX;
                    int y = 128 >> subY;
                    int width = 32 >> subX;
                    int rows = 16 >> subY;
                    for (int phase = 0; phase < 4; phase++)
                    {
                        int vectorX = -322 - (phase & 1);
                        int vectorY = -64 - (phase >> 1);
                        T[] source = new T[stride * height];
                        for (int index = 0; index < source.Length; index++)
                        { source[index] = T.CreateChecked(random.Next(1 << depth)); }
                        T[] actual = source.ToArray();
                        T[] expected = source.ToArray();
                        int sourceX = x + (vectorX >> subX);
                        int sourceY = y + (vectorY >> subY);
                        int fx = (vectorX & ((1 << subX) - 1)) * 64;
                        int fy = (vectorY & ((1 << subY) - 1)) * 64;
                        for (int row = 0; row < rows; row++)
                        {
                            for (int column = 0; column < width; column++)
                            {
                                int first = (sourceY + row) * stride + sourceX + column;
                                int top = ((128 - fx) * int.CreateChecked(source[first]) + fx * int.CreateChecked(source[first + 1]) + 4) >> 3;
                                int bottom = ((128 - fx) * int.CreateChecked(source[first + stride]) + fx * int.CreateChecked(source[first + stride + 1]) + 4) >> 3;
                                expected[(y + row) * stride + x + column] = T.CreateChecked(((128 - fy) * top + fy * bottom + 1024) >> 11);
                            }
                        }
                        Av1IntraBlockCopy.Apply<T>(actual, stride, x, y, width, rows, vectorX, vectorY, subX, subY);
                        Assert.True(actual.AsSpan().SequenceEqual(expected), $"{depth}-bit phase {phase} with subsampling {subX},{subY}.");
                        Assert.True(GC.TryStartNoGCRegion(1024 * 1024));
                        long allocated;
                        long controlAllocated;
                        try
                        {
                            long before = GC.GetAllocatedBytesForCurrentThread();
                            for (int repetition = 0; repetition < 32; repetition++)
                            { Av1IntraBlockCopy.Apply<T>(actual, stride, x, y, width, rows, vectorX, vectorY, subX, subY); }
                            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
                            byte[] control = new byte[37];
                            controlAllocated = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
                            GC.KeepAlive(control);
                        }
                        finally
                        {
                            GC.EndNoGCRegion();
                        }
                        Assert.Equal(IntPtr.Size == 8 ? 64 : 52, controlAllocated);
                        Assert.True(allocated == 0, $"{depth}-bit phase {phase} with subsampling {subX},{subY} allocated {allocated} bytes after warmup.");
                    }
                }
            }
        }

    }
}
