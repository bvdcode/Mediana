// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9
{
    internal ref struct Vp9BooleanReader
    {
        private readonly ReadOnlySpan<byte> encoded;
        private int position = 1, available;
        private int cache, value, range = 255;

        internal Vp9BooleanReader(ReadOnlySpan<byte> encoded)
        {
            if (encoded.IsEmpty) { throw new InvalidDataException("Truncated VP9 arithmetic partition."); }
            this.encoded = encoded; value = encoded[0];
            if (Read()) { throw new InvalidDataException("The VP9 arithmetic marker must be zero."); }
        }

        internal bool Read(int probability = 128)
        {
            int split = 1 + (((range - 1) * probability) >> 8);
            bool result = value >= split;
            if (result) { value -= split; range -= split; } else { range = split; }
            int shift = BitOperations.LeadingZeroCount((uint)range) - 24;
            range <<= shift;
            while (shift > 0)
            {
                if (available == 0)
                {
                    if (position >= encoded.Length) { throw new InvalidDataException("Truncated VP9 arithmetic partition."); }
                    cache = encoded[position++]; available = 8;
                }
                int take = Math.Min(shift, available); available -= take;
                value = (value << take) | ((cache >> available) & ((1 << take) - 1)); shift -= take;
            }
            return result;
        }

        internal int Literal(int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++) { result = (result << 1) | (Read() ? 1 : 0); }
            return result;
        }

        internal int Tree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities)
        {
            int node = 0;
            do { node = tree[node + (Read(probabilities[node >> 1]) ? 1 : 0)]; } while (node > 0);
            return -node;
        }

        internal void Finish()
        {
            if ((cache & ((1 << available) - 1)) != 0 || encoded[position..].IndexOfAnyExcept((byte)0) >= 0)
            { throw new InvalidDataException("VP9 arithmetic partition padding must be zero."); }
        }
    }
}
