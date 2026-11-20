// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    internal static class H264StridedInterpolationTestInput
    {
        public static void Verify()
        {
            const int columns = 32;
            const int rows = 24;
            const int stride = 64;
            byte[] packed = new byte[columns * rows];
            ushort[] packed10 = new ushort[columns * rows];
            byte[] storage = new byte[stride * rows];
            ushort[] storage10 = new ushort[stride * rows];
            byte[] expected = new byte[256];
            byte[] actual = new byte[256];
            ushort[] expected10 = new ushort[256];
            ushort[] actual10 = new ushort[256];
            foreach (int origin in new[] { 0, columns })
            {
                storage.AsSpan().Fill(255);
                storage10.AsSpan().Fill(ushort.MaxValue);
                Span<byte> strided = storage.AsSpan(origin);
                Span<ushort> strided10 = storage10.AsSpan(origin);
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        int sample = (x * 37 + y * 93 + (x ^ y) * 19) & 1023;
                        packed[y * columns + x] = strided[y * stride + x] = (byte)sample;
                        packed10[y * columns + x] = strided10[y * stride + x] = (ushort)sample;
                    }
                }
                foreach ((int width, int height) in new[] { (4, 4), (8, 8), (16, 16), (16, 8), (8, 16) })
                {
                    Span<byte> a = actual.AsSpan(0, width * height);
                    Span<byte> e = expected.AsSpan(0, width * height);
                    Span<ushort> a10 = actual10.AsSpan(0, width * height);
                    Span<ushort> e10 = expected10.AsSpan(0, width * height);
                    foreach (int y in new[] { -20, -1, 0, 9, 23, 60 })
                    {
                        foreach (int x in new[] { -20, -1, 0, 7, 21, 31, 60 })
                        {
                            H264BorderExtension.Copy(packed, columns, columns, rows, x, y, width, height, e);
                            H264BorderExtension.Copy(strided, stride, columns, rows, x, y, width, height, a);
                            H264HighPrecisionInterpolation.Copy(packed10, columns, columns, rows, x, y, width, height, e10);
                            H264HighPrecisionInterpolation.Copy(strided10, stride, columns, rows, x, y, width, height, a10);
                            Require(a, e, a10, e10);
                            for (int dy = 0; dy < 8; dy++)
                            {
                                for (int dx = 0; dx < 8; dx++)
                                {
                                    H264ChromaInterpolation.Apply(packed, columns, columns, rows, x, y, dx, dy, width, height, e);
                                    H264ChromaInterpolation.Apply(strided, stride, columns, rows, x, y, dx, dy, width, height, a);
                                    H264HighPrecisionInterpolation.Chroma(packed10, columns, columns, rows, x, y, dx, dy, width, height, e10);
                                    H264HighPrecisionInterpolation.Chroma(strided10, stride, columns, rows, x, y, dx, dy, width, height, a10);
                                    Require(a, e, a10, e10);
                                    if (dx < 4 && dy < 4)
                                    {
                                        H264LumaInterpolation.Apply(packed, columns, columns, rows, x, y, dx, dy, width, height, e);
                                        H264LumaInterpolation.Apply(strided, stride, columns, rows, x, y, dx, dy, width, height, a);
                                        H264HighPrecisionInterpolation.Luma(packed10, columns, columns, rows, x, y, dx, dy, width, height, e10, 1023);
                                        H264HighPrecisionInterpolation.Luma(strided10, stride, columns, rows, x, y, dx, dy, width, height, a10, 1023);
                                        Require(a, e, a10, e10);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void Require(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected,
            ReadOnlySpan<ushort> actual10, ReadOnlySpan<ushort> expected10)
        {
            if (!actual.SequenceEqual(expected) || !actual10.SequenceEqual(expected10))
            {
                throw new InvalidDataException("Strided field interpolation read a sample outside its own rows.");
            }
        }
    }
}
