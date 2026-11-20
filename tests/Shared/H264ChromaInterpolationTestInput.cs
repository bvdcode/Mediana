// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Formats.Video;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Tests
{
    internal static class H264ChromaInterpolationTestInput
    {
        public static void Verify()
        {
            foreach (int chromaFormat in new[] { 1, 2 })
            {
                Verify<byte>(8, chromaFormat);
                Verify<ushort>(10, chromaFormat);
            }
        }

        private static void Verify<TSample>(int bitDepth, int chromaFormat) where TSample : unmanaged, IBinaryInteger<TSample>
        {
            int maximum = (1 << bitDepth) - 1;
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264Picture picture = new(new H264SequenceParameterSet
            {
                LumaBitDepth = bitDepth,
                ChromaBitDepth = bitDepth,
                ChromaFormat = chromaFormat,
                WidthInMbs = 2,
                HeightInMapUnits = 2,
                FrameMbsOnly = true,
                Width = 32,
                Height = 32
            }, allocator, H264SliceKind.P))
            {
                foreach (int plane in new[] { 1, 2 })
                {
                    Span<TSample> samples = MemoryMarshal.Cast<byte, TSample>(picture.Plane(plane));
                    for (int pattern = 0; pattern < 3; pattern++)
                    {
                        for (int index = 0; index < samples.Length; index++)
                        {
                            samples[index] = TSample.CreateChecked(pattern switch
                            {
                                0 => maximum,
                                1 => (index * 53 + index / 16 * 97 + index * index % 131 + plane * 29) & maximum,
                                2 => (index + index / 16) % 2 * maximum,
                                _ => throw new InvalidOperationException("Unknown chroma test pattern.")
                            });
                        }
                        TSample[] source = samples.ToArray();
                        foreach ((int width, int height) in new[] { (1, 1), (2, 3), (4, 4), (8, 8), (8, 16), (16, 8), (17, 19) })
                        {
                            foreach ((int x, int y, int motionX, int motionY) in new[]
                            {
                                (3, 4, 0, 0), (0, 0, 0, 0), (7, 0, 0, 0), (0, 7, 0, 0), (7, 7, 0, 0), (8, 8, 0, 0),
                                (0, 0, -8, -8), (14, 14, 8, 8), (-3, 6, 0, 0), (16, 16, 0, 0),
                                (int.MinValue, int.MaxValue, int.MaxValue - 7, int.MinValue),
                                (int.MaxValue, int.MinValue, int.MinValue, int.MaxValue - 7)
                            })
                            {
                                for (int dy = 0; dy < 8; dy++)
                                {
                                    for (int dx = 0; dx < 8; dx++)
                                    {
                                        VerifyBlock<TSample>(picture, source, plane, x, y, motionX + dx, motionY + dy, width, height);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Chroma interpolation retained picture storage.");
            }
        }

        private static void VerifyBlock<TSample>(H264Picture picture, ReadOnlySpan<TSample> source, int plane,
            int x, int y, int motionX, int motionY, int width, int height)
            where TSample : unmanaged, IBinaryInteger<TSample>
        {
            byte[] guarded = new byte[width * height * picture.SampleBytes + 2];
            Array.Fill(guarded, (byte)0xa5);
            Span<byte> output = guarded.AsSpan(1, width * height * picture.SampleBytes);
            H264MotionCompensation.Chroma(picture, plane, x, y, motionX, motionY, width, height, output);
            Span<TSample> pixels = MemoryMarshal.Cast<byte, TSample>(output);
            int rows = picture.Height / picture.ChromaVerticalScale;
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    long eighthX = 8 * ((long)x + column) + motionX;
                    long eighthY = 8 * ((long)y + row) + (long)motionY * (2 / picture.ChromaVerticalScale);
                    long ix = eighthX >> 3;
                    long iy = eighthY >> 3;
                    int dx = (int)(eighthX & 7);
                    int dy = (int)(eighthY & 7);
                    int expected = ((8 - dx) * (8 - dy) * Sample(source, ix, iy, rows) + dx * (8 - dy) * Sample(source, ix + 1, iy, rows)
                        + (8 - dx) * dy * Sample(source, ix, iy + 1, rows) + dx * dy * Sample(source, ix + 1, iy + 1, rows) + 32) >> 6;
                    if (int.CreateChecked(pixels[row * width + column]) != expected)
                    {
                        throw new InvalidDataException($"Chroma interpolation differs from scalar bilinear sampling: plane {plane}, " +
                            $"position {x},{y}, vector {motionX},{motionY}, block {width}x{height}, pixel {column},{row}.");
                    }
                }
            }
            if (guarded[0] != 0xa5 || guarded[^1] != 0xa5)
            {
                throw new InvalidDataException("Chroma interpolation wrote outside its destination.");
            }
        }

        private static int Sample<TSample>(ReadOnlySpan<TSample> samples, long x, long y, int rows)
            where TSample : unmanaged, IBinaryInteger<TSample> =>
            int.CreateChecked(samples[(int)Math.Clamp(y, 0, rows - 1) * 16 + (int)Math.Clamp(x, 0, 15)]);
    }
}
