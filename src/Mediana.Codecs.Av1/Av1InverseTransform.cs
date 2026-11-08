// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1InverseTransform
    {
        internal static void Inverse(Span<int> values, int width, int height, int type, int bitDepth = 8)
        {
            int rowMaximum = Maximum(bitDepth switch
            {
                8 => 16,
                10 => 18,
                12 => 20,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            });
            int columnMaximum = Maximum(Math.Max(bitDepth + 6, 16));
            int horizontal = Horizontal(type);
            int vertical = Vertical(type);
            ValidateSize(width, horizontal);
            ValidateSize(height, vertical);
            if (values.Length != width * height || width > height * 4 || height > width * 4)
            {
                throw new ArgumentException("The AV1 transform geometry is invalid.", nameof(values));
            }
            int rowShift = (width, height) switch
            {
                (4, 4) or (4, 8) or (8, 4) => 0,
                (8, 8) or (8, 16) or (16, 8) or (16, 32) or (32, 16) or (32, 64) or (64, 32) or
                    (4, 16) or (16, 4) => 1,
                (16, 16) or (32, 32) or (64, 64) or (8, 32) or (32, 8) or (16, 64) or (64, 16) => 2,
                _ => throw new ArgumentException("The AV1 transform geometry is invalid.", nameof(values))
            };
            bool scaleRectangle = Math.Abs(BitOperations.Log2((uint)width) - BitOperations.Log2((uint)height)) == 1;
            for (int row = 0; row < height; row++)
            {
                Span<int> samples = values.Slice(row * width, width);
                if (scaleRectangle)
                {
                    for (int index = 0; index < width; index++) { samples[index] = Round((long)samples[index] * 2896); }
                }
                TransformInRange(samples, horizontal, rowMaximum);
                for (int index = 0; index < width; index++)
                {
                    samples[index] = Math.Clamp(Round(samples[index], rowShift), -columnMaximum - 1, columnMaximum);
                }
            }
            Span<int> column = stackalloc int[height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++) { column[y] = values[y * width + x]; }
                TransformInRange(column[..height], vertical, columnMaximum);
                for (int y = 0; y < height; y++) { values[y * width + x] = Round(column[y], 4); }
            }
            if (type is 4 or 6 or 8 or 14)
            {
                for (int y = 0; y < height / 2; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int first = y * width + x;
                        int last = (height - 1 - y) * width + x;
                        (values[first], values[last]) = (values[last], values[first]);
                    }
                }
            }
            if (type is 5 or 6 or 7 or 15)
            {
                for (int y = 0; y < height; y++) { values.Slice(y * width, width).Reverse(); }
            }
        }

        internal static void Transform(Span<int> values, int type, int precision = 16)
        {
            TransformInRange(values, type, Maximum(precision));
        }

        private static void TransformInRange(Span<int> values, int type, int maximum)
        {
            ValidateSize(values.Length, type);
            switch (type)
            {
                case 0: Dct(values, maximum); break;
                case 1: Adst(values, maximum); break;
                case 2:
                    int multiplier = values.Length switch
                    {
                        4 => 5793,
                        8 => 8192,
                        16 => 11586,
                        32 => 16384,
                        _ => throw new InvalidOperationException("The AV1 identity transform size is invalid.")
                    };
                    for (int index = 0; index < values.Length; index++) { values[index] = Round((long)values[index] * multiplier); }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static int Horizontal(int type) => type switch
        {
            0 or 1 or 4 or 11 => 0,
            2 or 3 or 5 or 6 or 7 or 8 or 13 or 15 => 1,
            9 or 10 or 12 or 14 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static int Vertical(int type) => type switch
        {
            0 or 2 or 5 or 10 => 0,
            1 or 3 or 4 or 6 or 7 or 8 or 12 or 14 => 1,
            9 or 11 or 13 or 15 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        private static void ValidateSize(int size, int type)
        {
            bool valid = type switch
            {
                0 => size is 4 or 8 or 16 or 32 or 64,
                1 => size is 4 or 8 or 16,
                2 => size is 4 or 8 or 16 or 32,
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
            if (!valid) { throw new ArgumentException("The AV1 transform length is invalid.", nameof(size)); }
        }

        private static int Round(long value, int bits = 12) => bits == 0 ? checked((int)value) : checked((int)((value + (1L << (bits - 1))) >> bits));
        private static int Maximum(int precision) => precision switch
        {
            16 => 32767,
            18 => 131071,
            20 => 524287,
            _ => throw new ArgumentOutOfRangeException(nameof(precision))
        };

        private static int Clip(int value, int maximum) => Math.Clamp(value, -maximum - 1, maximum);

        private static void Validate(int value, int maximum)
        {
            if (value < -maximum - 1 || value > maximum)
            {
                throw new InvalidDataException("The AV1 transform intermediate exceeds its signed precision.");
            }
        }

        private static int Reverse(int bits, int value)
        {
            int result = 0;
            for (int index = 0; index < bits; index++) { result = (result << 1) | ((value >> index) & 1); }
            return result;
        }

        private static int Cosine(int angle)
        {
            ReadOnlySpan<ushort> values =
            [
                4096, 4095, 4091, 4085, 4076, 4065, 4052, 4036, 4017, 3996, 3973, 3948, 3920, 3889, 3857, 3822,
                3784, 3745, 3703, 3659, 3612, 3564, 3513, 3461, 3406, 3349, 3290, 3229, 3166, 3102, 3035, 2967,
                2896, 2824, 2751, 2675, 2598, 2520, 2440, 2359, 2276, 2191, 2106, 2019, 1931, 1842, 1751, 1660,
                1567, 1474, 1380, 1285, 1189, 1092, 995, 897, 799, 700, 601, 501, 401, 301, 201, 101, 0
            ];
            int index = angle & 255;
            return index switch
            {
                <= 64 => values[index],
                <= 128 => -values[128 - index],
                <= 192 => -values[index - 128],
                _ => values[256 - index]
            };
        }

        private static void Rotate(Span<int> values, int a, int b, int angle, bool flip, int maximum)
        {
            int cosine = Cosine(angle);
            int sine = Cosine(angle - 64);
            int first = Round((long)values[a] * cosine - (long)values[b] * sine);
            int second = Round((long)values[a] * sine + (long)values[b] * cosine);
            Validate(first, maximum); Validate(second, maximum);
            values[a] = flip ? second : first;
            values[b] = flip ? first : second;
        }

        private static void Hadamard(Span<int> values, int a, int b, bool flip, int maximum)
        {
            int first = values[a];
            int second = values[b];
            if (flip)
            {
                values[a] = Clip(second - first, maximum);
                values[b] = Clip(second + first, maximum);
            }
            else
            {
                values[a] = Clip(first + second, maximum);
                values[b] = Clip(first - second, maximum);
            }
        }
    }
}
