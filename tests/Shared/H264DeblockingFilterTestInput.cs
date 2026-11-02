// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264DeblockingFilterTestInput
    {
        public static void Verify()
        {
            H264DeblockingThresholds[] thresholds =
            [new(0, 0, 0, 0, 0), new(4, 2, 0, 0, 1), new(12, 4, 0, 1, 1),
                new(25, 8, 1, 2, 3), new(71, 12, 4, 6, 8), new(255, 18, 13, 17, 25)];
            Random random = new(8847);
            int branches = VerifyThresholds();
            Span<byte> strengths = stackalloc byte[4];
            for (int combination = 0; combination < 625; combination++)
            {
                int remainder = combination;
                for (int segment = 0; segment < 4; segment++)
                {
                    strengths[segment] = (byte)(remainder % 5);
                    remainder /= 5;
                }
                foreach (H264DeblockingThresholds limits in thresholds)
                {
                    foreach (bool vertical in new[] { false, true })
                    {
                        foreach (bool chroma in new[] { false, true })
                        {
                            for (int padding = 0; padding < 2; padding++)
                            {
                                branches |= VerifyEdge(random, strengths, limits, vertical, chroma, padding * 5);
                            }
                        }
                    }
                }
            }
            if ((branches & 127) != 127)
            {
                throw new InvalidDataException($"Deblocking cases did not exercise every filter branch: {branches}.");
            }
        }

        public static void VerifyAllocation()
        {
            byte[] pixels = new byte[16 * 8];
            Array.Fill(pixels, (byte)128);
            byte[] strengths = [1, 2, 3, 4];
            H264DeblockingThresholds limits = new(255, 18, 13, 17, 25);
            H264DeblockingFilter.Apply(pixels, 4 * 16, 16, false, false, strengths, limits);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1024; index++)
            {
                H264DeblockingFilter.Apply(pixels, 4 * 16, 16, false, false, strengths, limits);
                H264DeblockingFilter.Apply(pixels, 4, 8, true, false, strengths, limits);
            }
            if (GC.GetAllocatedBytesForCurrentThread() != before)
            {
                throw new InvalidDataException("Deblocking edge filtering allocated heap storage.");
            }
        }

        private static int VerifyEdge(Random random, ReadOnlySpan<byte> strengths, H264DeblockingThresholds limits,
            bool vertical, bool chroma, int padding)
        {
            int length = chroma ? 8 : 16;
            int stride = (vertical ? 8 : length) + padding;
            int rows = vertical ? length : 8;
            int size = stride * rows;
            byte[] actual = new byte[size + 32];
            random.NextBytes(actual);
            int start = vertical ? 4 : 4 * stride;
            int step = vertical ? 1 : stride;
            int along = vertical ? stride : 1;
            Span<byte> pixels = actual.AsSpan(13, size);
            for (int index = 0; index < length; index++)
            {
                int position = start + index * along;
                int basis = (index % 6) switch { 0 => 0, 1 => 255, _ => random.Next(32, 224) };
                for (int offset = -4; offset < 4; offset++)
                {
                    int value = basis + random.Next(-limits.Beta - 1, limits.Beta + 2);
                    pixels[position + offset * step] = (byte)Math.Clamp(value, 0, 255);
                }
                if (index % 6 is 0 or 1)
                {
                    pixels[position - step] = (byte)basis;
                    pixels[position] = (byte)basis;
                    pixels[position - 2 * step] = (byte)basis;
                    pixels[position + step] = (byte)Math.Clamp(basis + (basis == 0 ? 17 : -17), 0, 255);
                }
                else if (index % 6 == 2)
                {
                    pixels[position] = (byte)Math.Clamp(pixels[position - step] + limits.Alpha + index % 3 - 1, 0, 255);
                }
                else if (index % 6 == 3)
                {
                    pixels[position + step] = (byte)Math.Clamp(pixels[position] + limits.Beta + index % 3 - 1, 0, 255);
                }
            }
            return VerifyPixels(actual, size, stride, vertical, chroma, strengths, limits);
        }

        private static int VerifyPixels(byte[] actual, int size, int stride, bool vertical, bool chroma,
            ReadOnlySpan<byte> strengths, H264DeblockingThresholds limits)
        {
            int length = chroma ? 8 : 16;
            int start = vertical ? 4 : 4 * stride;
            int step = vertical ? 1 : stride;
            int along = vertical ? stride : 1;
            Span<byte> pixels = actual.AsSpan(13, size);
            byte[] expected = (byte[])actual.Clone();
            int branches = 0;
            for (int index = 0; index < length; index++)
            {
                int strength = strengths[index / (chroma ? 2 : 4)];
                branches |= Reference(expected.AsSpan(13, size), start + index * along, step, strength, chroma, limits);
            }
            H264DeblockingFilter.Apply(pixels, start, stride, vertical, chroma, strengths, limits);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException($"Deblocking differs from the independent equations: vertical={vertical}, chroma={chroma}, alpha={limits.Alpha}, beta={limits.Beta}.");
            }
            return branches;
        }

        private static int VerifyThresholds()
        {
            int branches = 0;
            Span<byte> strengths = stackalloc byte[4];
            foreach (H264DeblockingThresholds limits in new H264DeblockingThresholds[] { new(25, 8, 1, 2, 3), new(71, 12, 4, 6, 8) })
            {
                foreach (bool vertical in new[] { false, true })
                {
                    foreach (bool chroma in new[] { false, true })
                    {
                        int length = chroma ? 8 : 16;
                        int stride = vertical ? 8 : length;
                        int size = stride * (vertical ? length : 8);
                        int start = vertical ? 4 : 4 * stride;
                        int step = vertical ? 1 : stride;
                        int along = vertical ? stride : 1;
                        for (byte strength = 0; strength <= 4; strength++)
                        {
                            strengths.Fill(strength);
                            for (int condition = 0; condition < 6; condition++)
                            {
                                byte[] pixels = new byte[size + 32];
                                pixels.AsSpan(13, size).Fill(128);
                                for (int index = 0; index < length; index++)
                                {
                                    int position = 13 + start + index * along;
                                    int adjustment = index % 3 - 1;
                                    if (condition is 0 or 5)
                                    {
                                        int threshold = condition == 0 ? limits.Alpha : (limits.Alpha >> 2) + 2;
                                        for (int offset = 0; offset < 4; offset++)
                                        {
                                            pixels[position + offset * step] = (byte)(128 + threshold + adjustment);
                                        }
                                    }
                                    else
                                    {
                                        int offset = condition switch { 1 => -2, 2 => 1, 3 => -3, 4 => 2, _ => throw new InvalidDataException() };
                                        pixels[position + offset * step] = (byte)(128 + limits.Beta + adjustment);
                                    }
                                }
                                branches |= VerifyPixels(pixels, size, stride, vertical, chroma, strengths, limits);
                            }
                        }
                    }
                }
            }
            return branches;
        }

        private static int Reference(Span<byte> pixels, int position, int step, int strength, bool chroma,
            H264DeblockingThresholds limits)
        {
            int[] p = new int[4];
            int[] q = new int[4];
            for (int index = 0; index < 4; index++)
            {
                p[index] = pixels[position - (index + 1) * step];
                q[index] = pixels[position + index * step];
            }
            if (strength == 0 || Math.Abs(p[0] - q[0]) >= limits.Alpha ||
                Math.Abs(p[1] - p[0]) >= limits.Beta || Math.Abs(q[1] - q[0]) >= limits.Beta)
            {
                return 1;
            }
            int[] resultP = (int[])p.Clone();
            int[] resultQ = (int[])q.Clone();
            int branches = 0;
            if (strength == 4)
            {
                for (int side = 0; side < 2; side++)
                {
                    int[] a = side == 0 ? p : q;
                    int[] b = side == 0 ? q : p;
                    int[] output = side == 0 ? resultP : resultQ;
                    if (!chroma && Math.Abs(a[2] - a[0]) < limits.Beta && Math.Abs(a[0] - b[0]) < (limits.Alpha >> 2) + 2)
                    {
                        output[0] = (a[2] + 2 * a[1] + 2 * a[0] + 2 * b[0] + b[1] + 4) / 8;
                        output[1] = (a[2] + a[1] + a[0] + b[0] + 2) / 4;
                        output[2] = (2 * a[3] + 3 * a[2] + a[1] + a[0] + b[0] + 4) / 8;
                        branches |= 8;
                    }
                    else
                    {
                        output[0] = (2 * a[1] + a[0] + b[1] + 2) / 4;
                        branches |= 16;
                    }
                }
            }
            else
            {
                int tc0 = strength switch { 1 => limits.First, 2 => limits.Second, 3 => limits.Third, _ => throw new InvalidDataException() };
                bool ap = !chroma && Math.Abs(p[2] - p[0]) < limits.Beta;
                bool aq = !chroma && Math.Abs(q[2] - q[0]) < limits.Beta;
                int tc = tc0 + (chroma ? 1 : (ap ? 1 : 0) + (aq ? 1 : 0));
                int delta = (4 * (q[0] - p[0]) + p[1] - q[1] + 4) >> 3;
                delta = Math.Clamp(delta, -tc, tc);
                resultP[0] = Math.Clamp(p[0] + delta, 0, 255);
                resultQ[0] = Math.Clamp(q[0] - delta, 0, 255);
                branches |= delta < 0 ? 2 : 4;
                if (p[0] + delta is < 0 or > 255 || q[0] - delta is < 0 or > 255)
                {
                    branches |= 32;
                }
                int average = (p[0] + q[0] + 1) / 2;
                if (ap)
                {
                    resultP[1] += Math.Clamp((p[2] + average - 2 * p[1]) >> 1, -tc0, tc0);
                    branches |= 64;
                }
                if (aq)
                {
                    resultQ[1] += Math.Clamp((q[2] + average - 2 * q[1]) >> 1, -tc0, tc0);
                    branches |= 64;
                }
            }
            for (int index = 0; index < 3; index++)
            {
                pixels[position - (index + 1) * step] = (byte)resultP[index];
                pixels[position + index * step] = (byte)resultQ[index];
            }
            return branches;
        }
    }
}
