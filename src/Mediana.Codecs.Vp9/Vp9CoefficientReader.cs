// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9CoefficientReader
    {
        internal static int Read(ref Vp9BooleanReader bits, scoped Span<int> output, int size, Vp9TransformType type, int depth,
            scoped ReadOnlySpan<byte> probabilities, int baseContext, int initialContext, Vp9CoefficientCounts? counts)
        {
            ReadOnlySpan<ushort> scan = Vp9Scan.Get(size, type);
            Span<byte> cache = stackalloc byte[scan.Length]; output = output[..scan.Length]; output.Clear();
            int side = 4 << size, end = 0; bool checkEnd = true;
            for (int c = 0; c < scan.Length; c++)
            {
                int position = scan[c], context;
                if (c == 0) { context = initialContext; }
                else
                {
                    int row = position / side, column = position % side, a, b;
                    if (row == 0) { a = b = position - 1; }
                    else if (column == 0) { a = b = position - side; }
                    else
                    {
                        (a, b) = type switch
                        {
                            Vp9TransformType.DctDct or Vp9TransformType.AdstAdst => (position - side, position - 1),
                            Vp9TransformType.AdstDct => (position - 1, position - 1),
                            Vp9TransformType.DctAdst => (position - side, position - side),
                            _ => throw new InvalidDataException("Invalid VP9 transform type.")
                        };
                    }
                    context = (1 + cache[a] + cache[b]) >> 1;
                }
                int band = size == 0 ? Bands4[c] : c < 21 ? BandsLarge[c] : 5;
                int index = baseContext + band * 6 + context, offset = index * 3;
                if (checkEnd)
                {
                    bool more = bits.Read(probabilities[offset]);
                    if (counts is not null) { counts.More[index * 2 + (more ? 1 : 0)]++; }
                    if (!more) { break; }
                }
                int token;
                if (!bits.Read(probabilities[offset + 1])) { token = 0; }
                else if (!bits.Read(probabilities[offset + 2])) { token = 1; }
                else { token = ReadTail(ref bits, probabilities[offset + 2]); }
                if (counts is not null) { counts.Tokens[offset + Math.Min(2, token)]++; }
                cache[position] = Energy[token]; checkEnd = token != 0; end = c + 1;
                if (token == 0) { continue; }
                int coefficient = Magnitude(ref bits, token, depth);
                output[position] = bits.Read() ? -coefficient : coefficient;
            }
            return end;
        }

        private static int ReadTail(ref Vp9BooleanReader bits, int pivot)
        {
            int node = 4, row = (pivot - 1) >> 1;
            do
            {
                int index = row * 8 + (node >> 1) - 2, probability = Vp9Tables.Pareto[index];
                if ((pivot & 1) == 0) { probability = (probability + Vp9Tables.Pareto[index + 8]) >> 1; }
                node = Tree[node + (bits.Read(probability) ? 1 : 0)];
            } while (node > 0);
            return -node;
        }

        private static int Magnitude(ref Vp9BooleanReader bits, int token, int depth)
        {
            if (token < 5) { return token; }
            int category = token - 4, length = category == 6 ? 14 : category, value = Bases[category];
            if (category == 6)
            { for (int i = 0; i < depth - 8; i++) { if (bits.Read(255)) { value += 1 << (5 + depth - i); } } }
            ReadOnlySpan<byte> probabilities = Extra.Slice(Offsets[category], length);
            for (int i = 0; i < length; i++) { if (bits.Read(probabilities[i])) { value += 1 << (length - 1 - i); } }
            return value;
        }

        private static ReadOnlySpan<byte> Bands4 => [0, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 5, 5, 5];
        private static ReadOnlySpan<byte> BandsLarge => [0, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4];
        private static ReadOnlySpan<byte> Energy => [0, 1, 2, 3, 3, 4, 4, 5, 5, 5, 5];
        private static ReadOnlySpan<sbyte> Tree => [0, 2, -1, 4, 6, 10, -2, 8, -3, -4, 12, 14, -5, -6, 16, 18, -7, -8, -9, -10];
        private static ReadOnlySpan<byte> Bases => [0, 5, 7, 11, 19, 35, 67];
        private static ReadOnlySpan<byte> Offsets => [0, 0, 1, 3, 6, 10, 15];
        private static ReadOnlySpan<byte> Extra => [159, 165, 145, 173, 148, 140, 176, 155, 140, 135, 180, 157, 141, 134, 130,
            254, 254, 254, 252, 249, 243, 230, 196, 177, 153, 140, 133, 130, 129];
    }
}
