// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264LumaCandidateTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = new(new H264SequenceParameterSet
            {
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true,
                Width = 32,
                Height = 32
            }, allocator, H264SliceKind.P))
            {
                for (int index = 0; index < picture.Plane(0).Length; index++)
                {
                    picture.Plane(0)[index] = (byte)((index * 53 + index / 32 * 97 + index * index % 131) & 255);
                }
                byte[] original = picture.Plane(0).ToArray();
                byte[] samples = new byte[H264LumaNeighborhood.SampleBytes];
                byte[] filtered = new byte[H264LumaNeighborhood.FilteredBytes];
                byte[] expected = new byte[256];
                byte[] comparison = new byte[256];
                for (int index = 0; index < comparison.Length; index++)
                {
                    comparison[index] = (byte)((index * 131 + index / 16 * 71) & 255);
                }
                foreach ((int x, int y) in new[] { (-16, -16), (0, 0), (8, 8), (16, 16), (32, 32) })
                {
                    H264LumaNeighborhood neighborhood = new(picture, x, y, samples, filtered);
                    byte[] unchangedSamples = (byte[])samples.Clone();
                    byte[] unchangedFiltered = (byte[])filtered.Clone();
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            for (int index = 0; index < expected.Length; index++)
                            {
                                expected[index] = (byte)H264MotionInterpolationTestInput.Pixel(original,
                                    4L * (x + index % 16) + dx, 4L * (y + index / 16) + dy);
                            }
                            VerifyLimits(neighborhood, dx, dy, comparison, expected);
                            VerifyLimit(neighborhood, dx, dy, expected, expected, 0);
                        }
                    }
                    if (!samples.AsSpan().SequenceEqual(unchangedSamples) || !filtered.AsSpan().SequenceEqual(unchangedFiltered))
                    {
                        throw new InvalidDataException("Candidate evaluation changed its prepared neighborhood.");
                    }
                }
                if (!picture.Plane(0).SequenceEqual(original))
                {
                    throw new InvalidDataException("Candidate evaluation changed the reference picture.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Candidate evaluation retained picture storage.");
            }
        }

        private static void VerifyLimits(H264LumaNeighborhood neighborhood, int x, int y, byte[] comparison, byte[] expected)
        {
            VerifyLimit(neighborhood, x, y, comparison, expected, -1);
            VerifyLimit(neighborhood, x, y, comparison, expected, int.MaxValue);
            int error = 0;
            for (int index = 0; index < 256; index++)
            {
                error += Math.Abs(comparison[index] - expected[index]);
                if (index % 16 == 15)
                {
                    VerifyLimit(neighborhood, x, y, comparison, expected, error - 1);
                    VerifyLimit(neighborhood, x, y, comparison, expected, error);
                }
            }
        }

        private static void VerifyLimit(H264LumaNeighborhood neighborhood, int x, int y, byte[] comparison, byte[] expected, int limit)
        {
            byte[] guarded = new byte[262];
            Array.Fill(guarded, (byte)183);
            byte[] unchanged = (byte[])comparison.Clone();
            int actual = neighborhood.Evaluate(x, y, comparison, guarded.AsSpan(3, 256), limit);
            int error = 0;
            int count = 0;
            do
            {
                for (int column = 0; column < 16; column++)
                {
                    error += Math.Abs(comparison[count] - expected[count]);
                    count++;
                }
            } while (count < 256 && error <= limit);
            if (actual != error || !guarded.AsSpan(3, count).SequenceEqual(expected.AsSpan(0, count))
                || guarded.AsSpan(0, 3).ContainsAnyExcept((byte)183) || guarded.AsSpan(3 + count).ContainsAnyExcept((byte)183)
                || !comparison.AsSpan().SequenceEqual(unchanged))
            {
                throw new InvalidDataException("Candidate evaluation changed its row rejection boundary, prediction prefix, input or output guards.");
            }
        }
    }
}
