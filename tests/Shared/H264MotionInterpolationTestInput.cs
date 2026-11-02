// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Tests
{
    internal static class H264MotionInterpolationTestInput
    {
        private static ReadOnlySpan<int> Filter => [1, -5, 20, 20, -5, 1];

        public static void Verify()
        {
            VerifyAverages();
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
                for (int pattern = 0; pattern < 3; pattern++)
                {
                    for (int index = 0; index < picture.Plane(0).Length; index++)
                    {
                        picture.Plane(0)[index] = pattern switch
                        {
                            0 => (byte)((index * 53 + index / 32 * 97 + index * index % 131) & 255),
                            1 => (byte)((index + index / 32) % 2 * 255),
                            2 => (byte)((index / 32 < 16 && index % 32 < 16) ? 0 : 255),
                            _ => throw new InvalidOperationException("Unknown interpolation test pattern.")
                        };
                    }
                    byte[] source = picture.Plane(0).ToArray();
                    foreach ((int width, int height) in new[]
                    {
                        (1, 1), (3, 5), (4, 4), (4, 8), (7, 5), (8, 4), (8, 8), (8, 16),
                        (9, 7), (12, 11), (15, 9), (16, 8), (16, 16), (17, 19)
                    })
                    {
                        foreach ((int x, int y, int motionX, int motionY) in new[]
                        {
                            (8, 8, 0, 0), (0, 0, -4, -4), (28, 28, 4, 4), (-8, 8, 0, 0), (8, -8, 0, 0),
                            (40, 8, 0, 0), (8, 40, 0, 0), (int.MinValue, int.MaxValue, 0, 0), (int.MaxValue, int.MinValue, 0, 0),
                            (8, 8, int.MinValue, int.MaxValue - 3), (8, 8, int.MaxValue - 3, int.MinValue)
                        })
                        {
                            for (int dy = 0; dy < 4; dy++)
                            {
                                for (int dx = 0; dx < 4; dx++)
                                {
                                    VerifyBlock(picture, source, x, y, motionX + dx, motionY + dy, width, height);
                                }
                            }
                        }
                    }
                    if (!picture.Plane(0).SequenceEqual(source))
                    {
                        throw new InvalidOperationException("Motion interpolation modified its reference picture.");
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Motion interpolation retained its reference storage.");
            }
        }

        private static void VerifyAverages()
        {
            byte[] left = new byte[Vector128<byte>.Count];
            byte[] right = new byte[Vector128<byte>.Count];
            for (int first = 0; first < 256; first++)
            {
                for (int second = 0; second < 256; second += Vector128<byte>.Count)
                {
                    for (int index = 0; index < left.Length; index++)
                    {
                        left[index] = (byte)((first + 31 * index) & 255);
                        right[index] = (byte)(second + index);
                    }
                    Vector128<byte> actual = H264SampleAverage.Rounded(
                        Vector128.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(left)),
                        Vector128.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(right)));
                    for (int index = 0; index < left.Length; index++)
                    {
                        if (actual.GetElement(index) != (left[index] + right[index] + 1) / 2)
                        {
                            throw new InvalidDataException("Sample averaging differs from rounded integer averaging.");
                        }
                    }
                }
            }
        }

        private static void VerifyBlock(H264Picture picture, byte[] source, int x, int y, int motionX, int motionY, int width, int height)
        {
            const byte guard = 183;
            byte[] storage = new byte[width * height + 6];
            storage.AsSpan().Fill(guard);
            Span<byte> output = storage.AsSpan(3, width * height);
            H264MotionCompensation.Luma(picture, x, y, motionX, motionY, width, height, output);
            for (int index = 0; index < 3; index++)
            {
                if (storage[index] != guard || storage[^(index + 1)] != guard)
                {
                    throw new InvalidDataException("Motion interpolation wrote outside the supplied output.");
                }
            }
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    long quarterX = 4L * ((long)x + column) + motionX;
                    long quarterY = 4L * ((long)y + row) + motionY;
                    int expected = Pixel(source, quarterX, quarterY);
                    if (output[row * width + column] != expected)
                    {
                        throw new InvalidDataException($"Interpolation differs from scalar convolution at {x},{y} + {motionX},{motionY}, " +
                            $"block {width}x{height}, pixel {column},{row}: expected {expected}, actual {output[row * width + column]}.");
                    }
                }
            }
        }

        internal static int Pixel(ReadOnlySpan<byte> source, long quarterX, long quarterY)
        {
            long x = quarterX >> 2;
            long y = quarterY >> 2;
            return ((int)(quarterX & 3), (int)(quarterY & 3)) switch
            {
                (0, 0) => Sample(source, x, y),
                (1, 0) => Average(Sample(source, x, y), Horizontal(source, x, y)),
                (2, 0) => Horizontal(source, x, y),
                (3, 0) => Average(Horizontal(source, x, y), Sample(source, x + 1, y)),
                (0, 1) => Average(Sample(source, x, y), Vertical(source, x, y)),
                (1, 1) => Average(Horizontal(source, x, y), Vertical(source, x, y)),
                (2, 1) => Average(Horizontal(source, x, y), Diagonal(source, x, y)),
                (3, 1) => Average(Horizontal(source, x, y), Vertical(source, x + 1, y)),
                (0, 2) => Vertical(source, x, y),
                (1, 2) => Average(Vertical(source, x, y), Diagonal(source, x, y)),
                (2, 2) => Diagonal(source, x, y),
                (3, 2) => Average(Diagonal(source, x, y), Vertical(source, x + 1, y)),
                (0, 3) => Average(Vertical(source, x, y), Sample(source, x, y + 1)),
                (1, 3) => Average(Vertical(source, x, y), Horizontal(source, x, y + 1)),
                (2, 3) => Average(Diagonal(source, x, y), Horizontal(source, x, y + 1)),
                (3, 3) => Average(Vertical(source, x + 1, y), Horizontal(source, x, y + 1)),
                _ => throw new InvalidOperationException("Invalid test phase.")
            };
        }

        private static int HorizontalSum(ReadOnlySpan<byte> source, long x, long y)
        {
            int sum = 0;
            for (int tap = 0; tap < 6; tap++)
            {
                sum += Filter[tap] * Sample(source, x + tap - 2, y);
            }
            return sum;
        }

        private static int Horizontal(ReadOnlySpan<byte> source, long x, long y) =>
            Math.Clamp((HorizontalSum(source, x, y) + 16) >> 5, 0, 255);

        private static int Vertical(ReadOnlySpan<byte> source, long x, long y)
        {
            int sum = 0;
            for (int tap = 0; tap < 6; tap++)
            {
                sum += Filter[tap] * Sample(source, x, y + tap - 2);
            }
            return Math.Clamp((sum + 16) >> 5, 0, 255);
        }

        private static int Diagonal(ReadOnlySpan<byte> source, long x, long y)
        {
            int sum = 0;
            for (int tap = 0; tap < 6; tap++)
            {
                sum += Filter[tap] * HorizontalSum(source, x, y + tap - 2);
            }
            return Math.Clamp((sum + 512) >> 10, 0, 255);
        }

        private static int Sample(ReadOnlySpan<byte> source, long x, long y) =>
            source[(int)Math.Clamp(y, 0, 31) * 32 + (int)Math.Clamp(x, 0, 31)];

        private static int Average(int a, int b) => (a + b + 1) >> 1;
    }
}
