// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using System.Runtime.Intrinsics;

namespace Mediana.Tests
{
    internal static class H264ShortInterpolationTestInput
    {
        private const int Minimum = -2550;
        private const int Maximum = 10710;
        private const int Lanes = 8;
        private static ReadOnlySpan<int> Taps => [1, -5, 20, 20, -5, 1];

        public static int Verify()
        {
            short[] storage = new short[96];
            short[] original = new short[storage.Length];
            Random random = new(29173);
            int cases = 0;
            foreach (int stride in new[] { 8, 13 })
            {
                foreach (int offset in new[] { 0, 3 })
                {
                    for (int pattern = 0; pattern < 64; pattern++)
                    {
                        storage.AsSpan().Fill(173);
                        for (int row = 0; row < Taps.Length; row++)
                        {
                            for (int lane = 0; lane < Lanes; lane++)
                            {
                                int mixed = (pattern + lane * 17) & 63;
                                storage[offset + row * stride + lane] = (short)(((mixed >> row) & 1) == 0 ? Minimum : Maximum);
                            }
                        }
                        Check(storage, original, offset, stride);
                        cases++;
                    }
                    for (int position = 0; position < Taps.Length; position++)
                    {
                        for (int value = Minimum; value <= Maximum; value++)
                        {
                            storage.AsSpan().Fill(173);
                            for (int row = 0; row < Taps.Length; row++)
                            {
                                for (int lane = 0; lane < Lanes; lane++)
                                {
                                    int other = (row * 2053 + lane * 1171 + value - Minimum) % (Maximum - Minimum + 1) + Minimum;
                                    storage[offset + row * stride + lane] = (short)(row == position ? value : other);
                                }
                            }
                            Check(storage, original, offset, stride);
                            cases++;
                        }
                    }
                    for (int iteration = 0; iteration < 4096; iteration++)
                    {
                        for (int row = 0; row < Taps.Length; row++)
                        {
                            for (int lane = 0; lane < Lanes; lane++)
                            {
                                storage[offset + row * stride + lane] = (short)random.Next(Minimum, Maximum + 1);
                            }
                        }
                        Check(storage, original, offset, stride);
                        cases++;
                    }
                }
            }
            Repeat(storage);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int checksum = Repeat(storage);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated != 0 || checksum != 255 * 1024)
            {
                throw new InvalidDataException($"Short interpolation allocated {allocated} bytes or changed constant results.");
            }
            return cases;
        }

        private static void Check(short[] storage, short[] original, int offset, int stride)
        {
            storage.CopyTo(original, 0);
            ReadOnlySpan<short> source = storage.AsSpan(offset, 5 * stride + Lanes);
            Vector128<byte> actual = H264LumaFilter.Diagonal8(source, stride, 0);
            for (int lane = 0; lane < Lanes; lane++)
            {
                long sum = 0;
                for (int row = 0; row < Taps.Length; row++)
                {
                    sum += (long)Taps[row] * source[row * stride + lane];
                }
                int expected = (int)Math.Clamp((sum + 512) >> 10, 0, 255);
                if (actual.GetElement(lane) != expected || H264LumaFilter.Diagonal(source, stride, lane) != expected)
                {
                    throw new InvalidDataException("Short diagonal interpolation differs from the wide scalar convolution.");
                }
            }
            if (!storage.AsSpan().SequenceEqual(original))
            {
                throw new InvalidDataException("Short diagonal interpolation changed its source storage.");
            }
        }

        private static int Repeat(Span<short> source)
        {
            source.Fill(Maximum);
            int checksum = 0;
            for (int iteration = 0; iteration < 1024; iteration++)
            {
                checksum += H264LumaFilter.Diagonal8(source[..48], Lanes, 0).GetElement(0);
            }
            return checksum;
        }
    }
}
