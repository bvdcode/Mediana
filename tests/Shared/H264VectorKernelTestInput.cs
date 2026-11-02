// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using System.Runtime.Intrinsics;

namespace Mediana.Tests
{
    internal static class H264VectorKernelTestInput
    {
        public static void Verify()
        {
            VerifyHalfwordRounding();
            VerifyDiagonalFiltering();
            VerifySampleAccess();
            VerifyAllocation();
        }

        private static void VerifyHalfwordRounding()
        {
            for (int value = short.MinValue; value <= short.MaxValue; value += 8)
            {
                Vector128<short> input = Vector128.Create((short)value, (short)(value + 1), (short)(value + 2), (short)(value + 3),
                    (short)(value + 4), (short)(value + 5), (short)(value + 6), (short)(value + 7));
                Vector128<byte> actual = H264LumaFilter.Half8(input);
                for (int lane = 0; lane < 8; lane++)
                {
                    int expected = Math.Clamp(unchecked((short)(value + lane + 16)) >> 5, 0, 255);
                    if (actual.GetElement(lane) != expected || actual.GetElement(lane + 8) != 0)
                    {
                        throw new InvalidDataException("The half-pixel filter changed rounding, clipping or lane extent.");
                    }
                }
            }
        }

        private static void VerifyDiagonalFiltering()
        {
            int[] samples = new int[96];
            int[] copy = new int[96];
            uint state = 96139457;
            for (int test = 0; test < 8192; test++)
            {
                for (int index = 0; index < samples.Length; index++)
                {
                    state = unchecked(state * 1664525 + 1013904223);
                    samples[index] = test switch
                    {
                        0 => 0,
                        1 => int.MinValue,
                        2 => int.MaxValue,
                        3 => (index & 1) == 0 ? int.MinValue : int.MaxValue,
                        4 => (index & 1) == 0 ? -2550 : 10710,
                        _ => unchecked((int)state)
                    };
                }
                samples.CopyTo(copy, 0);
                int stride = (test & 1) == 0 ? 8 : 13;
                Vector128<byte> actual = H264LumaFilter.Diagonal8(samples, stride, 1);
                Vector128<byte> four = H264LumaFilter.Diagonal4Samples(samples, stride, 1);
                for (int lane = 0; lane < 8; lane++)
                {
                    int position = 1 + lane;
                    int expected = unchecked(samples[position] - 5 * samples[position + stride] + 20 * samples[position + 2 * stride] +
                        20 * samples[position + 3 * stride] - 5 * samples[position + 4 * stride] + samples[position + 5 * stride] + 512) >> 10;
                    expected = Math.Clamp(expected, 0, 255);
                    if (actual.GetElement(lane) != expected || actual.GetElement(lane + 8) != 0 ||
                        (lane < 4 && four.GetElement(lane) != expected) || (lane >= 4 && four.GetElement(lane) != 0))
                    {
                        throw new InvalidDataException("Diagonal filtering changed signed arithmetic, rounding, saturation or lane extent.");
                    }
                }
                if (!samples.AsSpan().SequenceEqual(copy))
                {
                    throw new InvalidDataException("The diagonal filter changed its input.");
                }
            }
        }

        private static void VerifySampleAccess()
        {
            byte[] bytes = new byte[40];
            byte[] output = new byte[40];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)(index * 31 + 7);
            }
            for (int position = 0; position <= bytes.Length - 8; position++)
            {
                output.AsSpan().Fill(183);
                Vector128<byte> value = H264VectorSamples.Read8(bytes, position);
                H264VectorSamples.Write8(output, position, value);
                for (int index = 0; index < output.Length; index++)
                {
                    byte expected = index >= position && index < position + 8 ? bytes[index] : (byte)183;
                    if (output[index] != expected)
                    {
                        throw new InvalidDataException("The sample-vector access exceeded its eight-byte region.");
                    }
                }
            }
        }

        private static void VerifyAllocation()
        {
            int[] samples = new int[96];
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = unchecked(index * 104729);
            }
            byte[] output = new byte[40];
            for (int iteration = 0; iteration < 1000; iteration++)
            {
                H264VectorSamples.Write8(output, 1, H264LumaFilter.Diagonal8(samples, 13, 1));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 1000; iteration++)
            {
                H264VectorSamples.Write8(output, 1, H264LumaFilter.Diagonal8(samples, 13, 1));
            }
            if (GC.GetAllocatedBytesForCurrentThread() != allocated)
            {
                throw new InvalidOperationException("The interpolation kernel allocated managed storage.");
            }
        }
    }
}
