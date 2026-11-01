// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Tests
{
    internal static class H264LumaNeighborhoodTestInput
    {
        public static void Verify()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = Create(allocator))
            {
                byte[] samples = new byte[H264LumaNeighborhood.SampleBytes + 6];
                byte[] filtered = new byte[H264LumaNeighborhood.FilteredBytes + 6];
                byte[] guarded = new byte[262];
                for (int pattern = 0; pattern < 3; pattern++)
                {
                    for (int index = 0; index < picture.Plane(0).Length; index++)
                    {
                        picture.Plane(0)[index] = pattern switch
                        {
                            0 => (byte)((index * 53 + index / 32 * 97 + index * index % 131) & 255),
                            1 => (byte)((index + index / 32) % 2 * 255),
                            2 => (byte)((index / 32 < 16 && index % 32 < 16) ? 0 : 255),
                            _ => throw new InvalidOperationException("Unknown neighborhood test pattern.")
                        };
                    }
                    byte[] source = picture.Plane(0).ToArray();
                    foreach ((int x, int y) in new[] { (-16, -16), (0, 0), (8, 8), (16, 16), (32, 32), (40, -8), (int.MinValue, int.MaxValue), (int.MaxValue, int.MinValue), (1, 15) })
                    {
                        samples.AsSpan().Fill(183);
                        filtered.AsSpan().Fill(183);
                        H264LumaNeighborhood neighborhood = new(picture, x, y, samples.AsSpan(3, H264LumaNeighborhood.SampleBytes), filtered.AsSpan(3, H264LumaNeighborhood.FilteredBytes));
                        for (int dy = -3; dy <= 3; dy++)
                        {
                            for (int dx = -3; dx <= 3; dx++)
                            {
                                guarded.AsSpan().Fill(183);
                                neighborhood.Read(dx, dy, guarded.AsSpan(3, 256));
                                for (int row = 0; row < 16; row++)
                                {
                                    for (int column = 0; column < 16; column++)
                                    {
                                        int expected = H264MotionInterpolationTestInput.Pixel(source, 4L * ((long)x + column) + dx, 4L * ((long)y + row) + dy);
                                        if (guarded[3 + row * 16 + column] != expected)
                                        {
                                            throw new InvalidDataException("A prepared neighborhood differs from independent scalar convolution.");
                                        }
                                    }
                                }
                                Guard(guarded);
                            }
                        }
                        Guard(samples);
                        Guard(filtered);
                    }
                    if (!picture.Plane(0).SequenceEqual(source))
                    {
                        throw new InvalidDataException("Neighborhood interpolation changed its reference picture.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Neighborhood interpolation retained media storage.");
            }
        }

        public static void VerifyAllocation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = Create(allocator))
            {
                picture.Plane(0).Fill(73);
                byte[] samples = new byte[H264LumaNeighborhood.SampleBytes];
                byte[] filtered = new byte[H264LumaNeighborhood.FilteredBytes];
                byte[] output = new byte[256];
                Repeat(picture, samples, filtered, output);
                long before = GC.GetAllocatedBytesForCurrentThread();
                int checksum = Repeat(picture, samples, filtered, output);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (allocated != 0 || checksum != 73 * 256)
                {
                    throw new InvalidDataException($"Prepared luma neighborhoods allocated {allocated} heap bytes or changed constant samples.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Neighborhood allocation check retained media storage.");
            }
        }

        private static int Repeat(H264Picture picture, byte[] samples, byte[] filtered, byte[] output)
        {
            int sum = 0;
            for (int index = 0; index < 256; index++)
            {
                H264LumaNeighborhood neighborhood = new(picture, 8, 8, samples, filtered);
                neighborhood.Read(index % 7 - 3, index / 7 % 7 - 3, output);
                sum += output[0];
            }
            return sum;
        }

        private static H264Picture Create(MediaBufferAllocator allocator) => new(new H264SequenceParameterSet
        {
            WidthInMbs = 2,
            HeightInMapUnits = 2,
            FrameMbsOnly = true,
            Width = 32,
            Height = 32
        }, allocator, H264SliceKind.P);

        private static void Guard(byte[] storage)
        {
            if (storage.AsSpan(0, 3).ContainsAnyExcept((byte)183) || storage.AsSpan(storage.Length - 3).ContainsAnyExcept((byte)183))
            {
                throw new InvalidDataException("A prepared neighborhood wrote outside its supplied storage.");
            }
        }
    }
}
