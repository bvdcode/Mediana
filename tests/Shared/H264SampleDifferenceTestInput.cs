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
    }
}
