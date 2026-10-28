namespace Mediana.Codecs.H264
{
    internal static class H264IntraPrediction
    {
        public static void Luma4x4(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output) => Luma(mode, top, left, corner, output, 4);

        public static void Luma8x8(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output)
        {
            if ((!top.IsEmpty && top.Length != 16) || (!left.IsEmpty && left.Length != 8) || output.Length != 64)
            {
                throw new ArgumentException("H.264 intra 8x8 prediction requires 16 top, eight left and 64 output samples.");
            }
            Span<byte> filteredTop = stackalloc byte[16];
            Span<byte> filteredLeft = stackalloc byte[8];
            FilterReferences(top, corner, filteredTop);
            FilterReferences(left, corner, filteredLeft);
            byte? filteredCorner = corner;
            if (corner is byte value)
            {
                filteredCorner = (byte)Quarter(top.IsEmpty ? value : top[0], value, left.IsEmpty ? value : left[0]);
            }
            Luma(mode, top.IsEmpty ? [] : filteredTop, left.IsEmpty ? [] : filteredLeft, filteredCorner, output, 8);
        }

        private static void FilterReferences(ReadOnlySpan<byte> source, byte? corner, Span<byte> output)
        {
            if (source.IsEmpty)
            {
                return;
            }
            for (int index = 0; index < source.Length; index++)
            {
                int previous = index == 0 ? corner ?? source[0] : source[index - 1];
                output[index] = (byte)Quarter(previous, source[index], source[Math.Min(index + 1, source.Length - 1)]);
            }
        }

        private static void Luma(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output, int size)
        {
            bool needsTop = mode is 0 or 3 or 4 or 5 or 6 or 7;
            bool needsLeft = mode is 1 or 4 or 5 or 6 or 8;
            ValidateNeighbours(needsTop, needsLeft, mode is 4 or 5 or 6, top, left, corner);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int value = mode switch
                    {
                        0 => top[x],
                        1 => left[y],
                        2 => Dc(top[..Math.Min(top.Length, size)], left[..Math.Min(left.Length, size)]),
                        3 => Quarter(top[x + y], top[x + y + 1], top[Math.Min(x + y + 2, size * 2 - 1)]),
                        4 => Diagonal(x, y, top, left, corner!.Value),
                        5 => VerticalRight(x, y, top, left, corner!.Value),
                        6 => VerticalRight(y, x, left, top, corner!.Value),
                        7 => VerticalLeft(x, y, top),
                        8 => VerticalLeft(y, x, left),
                        _ => throw new InvalidDataException("Invalid H.264 directional intra prediction mode.")
                    };
                    output[y * size + x] = (byte)value;
                }
            }
        }

        public static void Luma16x16(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output)
        {
            ValidateNeighbours(mode is 0 or 3, mode is 1 or 3, mode == 3, top, left, corner);
            if (mode == 3)
            {
                Plane(top, left, corner!.Value, output, 16);
                return;
            }
            int dc = Dc(top, left);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    output[y * 16 + x] = mode switch
                    {
                        0 => top[x],
                        1 => left[y],
                        2 => (byte)dc,
                        _ => throw new InvalidDataException("Invalid H.264 intra 16x16 prediction mode.")
                    };
                }
            }
        }

        public static void Chroma8x8(int mode, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left,
            byte? corner, Span<byte> output)
        {
            ValidateNeighbours(mode is 2 or 3, mode is 1 or 3, mode == 3, top, left, corner);
            if (mode == 3)
            {
                Plane(top, left, corner!.Value, output, 8);
                return;
            }
            for (int blockY = 0; blockY < 8; blockY += 4)
            {
                for (int blockX = 0; blockX < 8; blockX += 4)
                {
                    ReadOnlySpan<byte> blockTop = top.IsEmpty ? [] : top.Slice(blockX, 4);
                    ReadOnlySpan<byte> blockLeft = left.IsEmpty ? [] : left.Slice(blockY, 4);
                    if (blockX > 0 && blockY == 0 && !blockTop.IsEmpty)
                    {
                        blockLeft = [];
                    }
                    if (blockY > 0 && blockX == 0 && !blockLeft.IsEmpty)
                    {
                        blockTop = [];
                    }
                    int dc = Dc(blockTop, blockLeft);
                    for (int y = blockY; y < blockY + 4; y++)
                    {
                        for (int x = blockX; x < blockX + 4; x++)
                        {
                            output[y * 8 + x] = mode switch
                            {
                                0 => (byte)dc,
                                1 => left[y],
                                2 => top[x],
                                _ => throw new InvalidDataException("Invalid H.264 chroma prediction mode.")
                            };
                        }
                    }
                }
            }
        }

        private static int Diagonal(int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
        {
            int difference = x - y;
            if (difference > 0)
            {
                return Quarter(Reference(top, corner, difference - 2), top[difference - 1], top[difference]);
            }
            if (difference < 0)
            {
                return Quarter(Reference(left, corner, -difference - 2), left[-difference - 1], left[-difference]);
            }
            return Quarter(top[0], corner, left[0]);
        }

        private static int VerticalRight(int x, int y, ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner)
        {
            int z = 2 * x - y;
            int position = x - (y >> 1);
            if (z >= 0 && (z & 1) == 0)
            {
                return Half(Reference(top, corner, position - 1), top[position]);
            }
            if (z > 0)
            {
                return Quarter(Reference(top, corner, position - 2), Reference(top, corner, position - 1), top[position]);
            }
            if (z == -1)
            {
                return Quarter(left[0], corner, top[0]);
            }
            return Quarter(left[-z - 1], left[-z - 2], Reference(left, corner, -z - 3));
        }

        private static int VerticalLeft(int x, int y, ReadOnlySpan<byte> samples)
        {
            int position = x + (y >> 1);
            int a = samples[Math.Min(position, samples.Length - 1)];
            int b = samples[Math.Min(position + 1, samples.Length - 1)];
            int c = samples[Math.Min(position + 2, samples.Length - 1)];
            return (y & 1) == 0 ? Half(a, b) : Quarter(a, b, c);
        }

        private static void Plane(ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte corner, Span<byte> output, int size)
        {
            int half = size / 2;
            int h = 0;
            int v = 0;
            for (int index = 0; index < half; index++)
            {
                h += (index + 1) * (top[half + index] - Reference(top, corner, half - 2 - index));
                v += (index + 1) * (left[half + index] - Reference(left, corner, half - 2 - index));
            }
            int factor = size == 16 ? 5 : 34;
            int b = (factor * h + 32) >> 6;
            int c = (factor * v + 32) >> 6;
            int a = 16 * (top[size - 1] + left[size - 1]);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    output[y * size + x] = (byte)Math.Clamp((a + b * (x - half + 1) + c * (y - half + 1) + 16) >> 5, 0, 255);
                }
            }
        }

        private static int Dc(ReadOnlySpan<byte> top, ReadOnlySpan<byte> left)
        {
            int count = top.Length + left.Length;
            if (count == 0)
            {
                return 128;
            }
            int sum = count / 2;
            foreach (byte value in top)
            {
                sum += value;
            }
            foreach (byte value in left)
            {
                sum += value;
            }
            return sum / count;
        }

        private static int Half(int a, int b) => (a + b + 1) >> 1;
        private static int Quarter(int a, int b, int c) => (a + 2 * b + c + 2) >> 2;
        private static int Reference(ReadOnlySpan<byte> samples, byte corner, int position) => position < 0 ? corner : samples[position];

        private static void ValidateNeighbours(bool needsTop, bool needsLeft, bool needsCorner,
            ReadOnlySpan<byte> top, ReadOnlySpan<byte> left, byte? corner)
        {
            if ((needsTop && top.IsEmpty) || (needsLeft && left.IsEmpty) || (needsCorner && corner is null))
            {
                throw new InvalidDataException("An H.264 intra prediction references unavailable neighbours.");
            }
        }
    }
}
