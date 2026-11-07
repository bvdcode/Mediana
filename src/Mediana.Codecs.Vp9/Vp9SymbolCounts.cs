// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9SymbolCounts
    {
        internal readonly uint[] Branches = new uint[4102];
        internal void Clear() => Array.Clear(Branches);

        internal bool Read(ref Vp9BooleanReader bits, Vp9ProbabilityContext probabilities, int index)
        {
            bool value = bits.Read(probabilities.Data[index]); Branches[index * 2 + (value ? 1 : 0)]++; return value;
        }

        internal int Tree(ref Vp9BooleanReader bits, ReadOnlySpan<sbyte> tree, Vp9ProbabilityContext probabilities, int offset)
        {
            int node = 0;
            do { node = tree[node + (Read(ref bits, probabilities, offset + (node >> 1)) ? 1 : 0)]; } while (node > 0);
            return -node;
        }

        internal void Record(int index, bool value) => Branches[index * 2 + (value ? 1 : 0)]++;
    }
}
