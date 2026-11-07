// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Vp9
{
    internal static partial class Vp9Transform
    {
        internal static void Inverse(Span<int> coefficients, int logSize, Vp9TransformType type, bool lossless)
        {
            if (logSize < 2 || logSize > 5 || (logSize == 5 && type != Vp9TransformType.DctDct) ||
                (lossless && (logSize != 2 || type != Vp9TransformType.DctDct)))
            { throw new InvalidDataException("Invalid VP9 transform configuration."); }
            (bool rowAdst, bool columnAdst) = type switch
            {
                Vp9TransformType.DctDct => (false, false), Vp9TransformType.AdstDct => (false, true),
                Vp9TransformType.DctAdst => (true, false), Vp9TransformType.AdstAdst => (true, true),
                _ => throw new InvalidDataException("Invalid VP9 transform type.")
            };
            int size = 1 << logSize, shift = Math.Min(6, logSize + 2);
            coefficients = coefficients[..(size * size)];
            for (int row = 0; row < size; row++)
            {
                Span<int> values = coefficients.Slice(row * size, size);
                if (lossless) { Walsh(values, 2); } else { OneDimension(values, logSize, rowAdst); }
            }
            Span<int> column = stackalloc int[32]; column = column[..size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++) { column[y] = coefficients[y * size + x]; }
                if (lossless) { Walsh(column, 0); } else { OneDimension(column, logSize, columnAdst); }
                for (int y = 0; y < size; y++)
                { coefficients[y * size + x] = lossless ? column[y] : (column[y] + (1 << (shift - 1))) >> shift; }
            }
        }

        internal static void OneDimension(Span<int> values, int logSize, bool adst)
        {
            Span<int> temporary = stackalloc int[32]; int size = 1 << logSize;
            values = values[..size]; values.CopyTo(temporary);
            if (adst)
            {
                switch (logSize)
                {
                    case 2: Adst4(values); return;
                    case 3:
                    case 4:
                        for (int i = 0; i < size / 2; i++) { values[i * 2] = temporary[size - 1 - i * 2]; values[i * 2 + 1] = temporary[i * 2]; }
                        if (logSize == 3) { Adst8(values); } else { Adst16(values); }
                        return;
                    default: throw new InvalidDataException("Invalid VP9 ADST size.");
                }
            }
            ReadOnlySpan<byte> permutation = logSize switch
            {
                2 => [0, 2, 1, 3], 3 => [0, 4, 2, 6, 1, 5, 3, 7],
                4 => [0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15],
                5 => [0, 16, 8, 24, 4, 20, 12, 28, 2, 18, 10, 26, 6, 22, 14, 30, 1, 17, 9, 25, 5, 21, 13, 29, 3, 19, 11, 27, 7, 23, 15, 31],
                _ => throw new InvalidDataException("Invalid VP9 DCT size.")
            };
            for (int i = 0; i < size; i++) { values[i] = temporary[permutation[i]]; }
            switch (logSize)
            {
                case 2: Dct4(values); break;
                case 3: Dct8(values); break;
                case 4: Dct16(values); break;
                case 5: Dct32(values); break;
                default: throw new InvalidDataException("Invalid VP9 DCT size.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Round(long value) => (int)((value + 8192) >> 14);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Rotate(Span<int> values, int a, int b, int cosine, int sine, bool flip)
        {
            int x = Round((long)values[a] * cosine - (long)values[b] * sine);
            int y = Round((long)values[a] * sine + (long)values[b] * cosine);
            values[a] = flip ? y : x; values[b] = flip ? x : y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Hadamard(Span<int> values, int a, int b)
        {
            int x = values[a], y = values[b]; values[a] = x + y; values[b] = x - y;
        }

        private static void Walsh(Span<int> values, int shift)
        {
            int a = values[0] >> shift, c = values[1] >> shift, d = values[2] >> shift, b = values[3] >> shift;
            a += c; d -= b; int e = (a - d) >> 1; b = e - b; c = e - c; a -= b; d += c;
            values[0] = a; values[1] = b; values[2] = c; values[3] = d;
        }
    }
}
