// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.Aac
{
    internal static class AacSpectralCosts
    {
        private const int PairMaximum = 12;
        private const int PairRadix = 2 * PairMaximum + 1;
        private const int QuadMaximum = 2;
        private const int QuadRadix = 2 * QuadMaximum + 1;
        private static readonly Vector256<int>[] PairCosts = CreatePairCosts();
        private static readonly Vector128<int>[] QuadCosts = CreateQuadCosts();

        public static (byte Book, int Bits) Select(ReadOnlySpan<int> values)
        {
            if ((values.Length & 3) != 0)
            {
                throw new ArgumentException("AAC spectral bands require a multiple of four coefficients.", nameof(values));
            }
            int maximum = 0;
            foreach (int value in values)
            {
                maximum = Math.Max(maximum, Math.Abs(value));
            }
            if (maximum == 0)
            {
                return (0, 0);
            }
            if (maximum > PairMaximum)
            {
                return (11, EscapeCost(values));
            }

            Vector256<int> pairs = Vector256<int>.Zero;
            for (int i = 0; i < values.Length; i += 2)
            {
                int index = (values[i] + PairMaximum) * PairRadix + values[i + 1] + PairMaximum;
                pairs += PairCosts[index];
            }
            Span<int> costs = stackalloc int[12];
            pairs.CopyTo(costs[4..]);
            if (maximum <= QuadMaximum)
            {
                Vector128<int> quads = Vector128<int>.Zero;
                for (int i = 0; i < values.Length; i += 4)
                {
                    int first = (values[i] + QuadMaximum) * QuadRadix + values[i + 1] + QuadMaximum;
                    int second = (values[i + 2] + QuadMaximum) * QuadRadix + values[i + 3] + QuadMaximum;
                    quads += QuadCosts[first * QuadRadix * QuadRadix + second];
                }
                quads.CopyTo(costs);
            }
            int firstBook = maximum switch { 1 => 1, 2 => 3, <= 4 => 5, <= 7 => 7, _ => 9 };
            int best = firstBook;
            for (int book = firstBook + 1; book <= 11; book++)
            {
                if (costs[book - 1] < costs[best - 1])
                {
                    best = book;
                }
            }
            return ((byte)best, costs[best - 1]);
        }

        private static int EscapeCost(ReadOnlySpan<int> values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i += 2)
            {
                int a = Math.Abs(values[i]);
                int b = Math.Abs(values[i + 1]);
                int symbol = Math.Min(a, 16) * 17 + Math.Min(b, 16);
                total += AacHuffmanEncoder.CountSymbol(11, symbol) + AdditionalBits(a) + AdditionalBits(b);
            }
            return total;
        }

        private static int AdditionalBits(int magnitude) => magnitude switch
        {
            0 => 0,
            < 16 => 1,
            _ => 2 * BitOperations.Log2((uint)magnitude) - 2
        };

        private static Vector256<int>[] CreatePairCosts()
        {
            Vector256<int>[] costs = new Vector256<int>[PairRadix * PairRadix];
            Span<int> tuple = stackalloc int[2];
            Span<int> lengths = stackalloc int[8];
            lengths[^1] = 0;
            for (int i = 0; i < costs.Length; i++)
            {
                tuple[0] = i / PairRadix - PairMaximum;
                tuple[1] = i % PairRadix - PairMaximum;
                for (int book = 5; book <= 11; book++)
                {
                    lengths[book - 5] = TupleCost(book, tuple);
                }
                costs[i] = Vector256.Create((ReadOnlySpan<int>)lengths);
            }
            return costs;
        }

        private static Vector128<int>[] CreateQuadCosts()
        {
            Vector128<int>[] costs = new Vector128<int>[QuadRadix * QuadRadix * QuadRadix * QuadRadix];
            Span<int> tuple = stackalloc int[4];
            for (int i = 0; i < costs.Length; i++)
            {
                int digits = i;
                for (int k = tuple.Length - 1; k >= 0; k--)
                {
                    tuple[k] = digits % QuadRadix - QuadMaximum;
                    digits /= QuadRadix;
                }
                costs[i] = Vector128.Create(TupleCost(1, tuple), TupleCost(2, tuple), TupleCost(3, tuple), TupleCost(4, tuple));
            }
            return costs;
        }

        // Unrepresentable books are excluded once per band using its maximum magnitude.
        private static int TupleCost(int book, ReadOnlySpan<int> tuple)
        {
            int bits = AacHuffmanEncoder.Count(book, tuple);
            return bits == int.MaxValue ? 0 : bits;
        }
    }
}
