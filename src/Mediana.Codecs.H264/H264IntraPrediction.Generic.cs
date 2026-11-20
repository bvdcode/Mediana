// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraPrediction<TSample> where TSample : unmanaged, IBinaryInteger<TSample>
    {
        public static void Luma4x4(int mode, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left,
            TSample? corner, Span<TSample> output, int maximum) => Luma(mode, top, left, corner, output, 4, maximum);

        public static void Luma8x8(int mode, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left,
            TSample? corner, Span<TSample> output, int maximum)
        {
            if ((!top.IsEmpty && top.Length != 16) || (!left.IsEmpty && left.Length != 8) || output.Length != 64)
            {
                throw new ArgumentException("H.264 intra 8x8 prediction requires 16 top, eight left and 64 output samples.");
            }
            Span<TSample> filteredTop = stackalloc TSample[16];
            Span<TSample> filteredLeft = stackalloc TSample[8];
            FilterReferences(top, corner, filteredTop);
            FilterReferences(left, corner, filteredLeft);
            TSample? filteredCorner = corner;
            if (corner is TSample value)
            {
                int sample = int.CreateTruncating(value);
                filteredCorner = TSample.CreateTruncating(Quarter(top.IsEmpty ? sample : Read(top, 0), sample, left.IsEmpty ? sample : Read(left, 0)));
            }
            Luma(mode, top.IsEmpty ? [] : filteredTop, left.IsEmpty ? [] : filteredLeft, filteredCorner, output, 8, maximum);
        }

        private static void FilterReferences(ReadOnlySpan<TSample> source, TSample? corner, Span<TSample> output)
        {
            if (source.IsEmpty)
            {
                return;
            }
            for (int index = 0; index < source.Length; index++)
            {
                int previous = int.CreateTruncating(index == 0 ? corner ?? source[0] : source[index - 1]);
                output[index] = TSample.CreateTruncating(Quarter(previous, Read(source, index), Read(source, Math.Min(index + 1, source.Length - 1))));
            }
        }

        private static void Luma(int mode, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left,
            TSample? corner, Span<TSample> output, int size, int maximum)
        {
            if (!IsLumaModeAvailable(mode, !top.IsEmpty, !left.IsEmpty, corner is not null))
            {
                throw new InvalidDataException("An H.264 intra prediction references unavailable neighbours.");
            }
            switch (mode)
            {
                case 0:
                    for (int row = 0; row < size; row++)
                    {
                        top[..size].CopyTo(output.Slice(row * size, size));
                    }
                    return;
                case 1:
                    for (int row = 0; row < size; row++)
                    {
                        output.Slice(row * size, size).Fill(left[row]);
                    }
                    return;
                case 2:
                    output[..(size * size)].Fill(TSample.CreateTruncating(Dc(top[..Math.Min(top.Length, size)], left[..Math.Min(left.Length, size)], maximum)));
                    return;
            }
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int value = mode switch
                    {
                        3 => Quarter(Read(top, x + y), Read(top, x + y + 1), Read(top, Math.Min(x + y + 2, size * 2 - 1))),
                        4 => Diagonal(x, y, top, left, corner!.Value),
                        5 => VerticalRight(x, y, top, left, corner!.Value),
                        6 => VerticalRight(y, x, left, top, corner!.Value),
                        7 => VerticalLeft(x, y, top),
                        8 => VerticalLeft(y, x, left),
                        _ => throw new InvalidDataException("Invalid H.264 directional intra prediction mode.")
                    };
                    output[y * size + x] = TSample.CreateTruncating(value);
                }
            }
        }

        public static bool IsLumaModeAvailable(int mode, bool hasTop, bool hasLeft, bool hasCorner) => mode switch
        {
            0 or 3 or 7 => hasTop,
            1 or 8 => hasLeft,
            2 => true,
            4 or 5 or 6 => hasTop && hasLeft && hasCorner,
            _ => throw new InvalidDataException("Invalid H.264 directional intra prediction mode.")
        };

        public static void Luma16x16(int mode, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left,
            TSample? corner, Span<TSample> output, int maximum)
        {
            ValidateNeighbours(mode is 0 or 3, mode is 1 or 3, mode == 3, top, left, corner);
            switch (mode)
            {
                case 0:
                    for (int row = 0; row < 16; row++)
                    {
                        top[..16].CopyTo(output.Slice(row * 16, 16));
                    }
                    return;
                case 1:
                    for (int row = 0; row < 16; row++)
                    {
                        output.Slice(row * 16, 16).Fill(left[row]);
                    }
                    return;
                case 2:
                    output[..256].Fill(TSample.CreateTruncating(Dc(top, left, maximum)));
                    return;
                case 3:
                    Plane(top, left, corner!.Value, output, 16, 16, maximum);
                    return;
                default:
                    throw new InvalidDataException("Invalid H.264 intra 16x16 prediction mode.");
            }
        }

        public static void Chroma(int mode, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left,
            TSample? corner, Span<TSample> output, int height, int maximum)
        {
            if (height is not (8 or 16) || output.Length != 8 * height)
            {
                throw new ArgumentException("Invalid H.264 chroma prediction dimensions.");
            }
            ValidateNeighbours(mode is 2 or 3, mode is 1 or 3, mode == 3, top, left, corner);
            if (mode == 3)
            {
                Plane(top, left, corner!.Value, output, 8, height, maximum);
                return;
            }
            for (int blockY = 0; blockY < height; blockY += 4)
            {
                for (int blockX = 0; blockX < 8; blockX += 4)
                {
                    ReadOnlySpan<TSample> blockTop = top.IsEmpty ? [] : top.Slice(blockX, 4);
                    ReadOnlySpan<TSample> blockLeft = left.IsEmpty ? [] : left.Slice(blockY, 4);
                    if (blockX > 0 && blockY == 0 && !blockTop.IsEmpty)
                    {
                        blockLeft = [];
                    }
                    if (blockY > 0 && blockX == 0 && !blockLeft.IsEmpty)
                    {
                        blockTop = [];
                    }
                    int dc = Dc(blockTop, blockLeft, maximum);
                    for (int y = blockY; y < blockY + 4; y++)
                    {
                        for (int x = blockX; x < blockX + 4; x++)
                        {
                            output[y * 8 + x] = mode switch
                            {
                                0 => TSample.CreateTruncating(dc),
                                1 => left[y],
                                2 => top[x],
                                _ => throw new InvalidDataException("Invalid H.264 chroma prediction mode.")
                            };
                        }
                    }
                }
            }
        }

        private static int Diagonal(int x, int y, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left, TSample corner)
        {
            int difference = x - y;
            if (difference > 0)
            {
                return Quarter(Reference(top, corner, difference - 2), Read(top, difference - 1), Read(top, difference));
            }
            if (difference < 0)
            {
                return Quarter(Reference(left, corner, -difference - 2), Read(left, -difference - 1), Read(left, -difference));
            }
            return Quarter(Read(top, 0), int.CreateTruncating(corner), Read(left, 0));
        }

        private static int VerticalRight(int x, int y, ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left, TSample corner)
        {
            int z = 2 * x - y;
            int position = x - (y >> 1);
            if (z >= 0 && (z & 1) == 0)
            {
                return Half(Reference(top, corner, position - 1), Read(top, position));
            }
            if (z > 0)
            {
                return Quarter(Reference(top, corner, position - 2), Reference(top, corner, position - 1), Read(top, position));
            }
            if (z == -1)
            {
                return Quarter(Read(left, 0), int.CreateTruncating(corner), Read(top, 0));
            }
            return Quarter(Read(left, -z - 1), Read(left, -z - 2), Reference(left, corner, -z - 3));
        }

        private static int VerticalLeft(int x, int y, ReadOnlySpan<TSample> samples)
        {
            int position = x + (y >> 1);
            int a = Read(samples, Math.Min(position, samples.Length - 1));
            int b = Read(samples, Math.Min(position + 1, samples.Length - 1));
            int c = Read(samples, Math.Min(position + 2, samples.Length - 1));
            return (y & 1) == 0 ? Half(a, b) : Quarter(a, b, c);
        }

        private static void Plane(ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left, TSample corner, Span<TSample> output, int size, int height, int maximum)
        {
            int half = size / 2;
            int halfHeight = height / 2;
            int h = 0;
            int v = 0;
            for (int index = 0; index < half; index++)
            {
                h += (index + 1) * (Read(top, half + index) - Reference(top, corner, half - 2 - index));
            }
            for (int index = 0; index < halfHeight; index++)
            {
                v += (index + 1) * (Read(left, halfHeight + index) - Reference(left, corner, halfHeight - 2 - index));
            }
            int factor = size == 16 ? 5 : 34;
            int b = (factor * h + 32) >> 6;
            int c = ((height == 16 ? 5 : 34) * v + 32) >> 6;
            int a = 16 * (Read(top, size - 1) + Read(left, height - 1));
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    output[y * size + x] = TSample.CreateTruncating(Math.Clamp((a + b * (x - half + 1) + c * (y - halfHeight + 1) + 16) >> 5, 0, maximum));
                }
            }
        }

        private static int Dc(ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left, int maximum)
        {
            int count = top.Length + left.Length;
            if (count == 0)
            {
                return (maximum + 1) / 2;
            }
            int sum = count / 2;
            foreach (TSample value in top)
            {
                sum += int.CreateTruncating(value);
            }
            foreach (TSample value in left)
            {
                sum += int.CreateTruncating(value);
            }
            return sum / count;
        }

        private static int Read(ReadOnlySpan<TSample> samples, int index) => int.CreateTruncating(samples[index]);

        private static int Half(int a, int b) => (a + b + 1) >> 1;
        private static int Quarter(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;
        private static int Reference(ReadOnlySpan<TSample> samples, TSample corner, int position) => position < 0 ? int.CreateTruncating(corner) : Read(samples, position);

        private static void ValidateNeighbours(bool needsTop, bool needsLeft, bool needsCorner,
            ReadOnlySpan<TSample> top, ReadOnlySpan<TSample> left, TSample? corner)
        {
            if ((needsTop && top.IsEmpty) || (needsLeft && left.IsEmpty) || (needsCorner && corner is null))
            {
                throw new InvalidDataException("An H.264 intra prediction references unavailable neighbours.");
            }
        }
    }
}
