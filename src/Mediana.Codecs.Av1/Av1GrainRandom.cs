// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1GrainRandom(ushort seed)
    {
        private int state = seed;

        internal int Next(int bits)
        {
            int next = ((state >> 0) ^ (state >> 1) ^ (state >> 3) ^ (state >> 12)) & 1;
            state = (state >> 1) | (next << 15);
            return (state >> (16 - bits)) & ((1 << bits) - 1);
        }
    }
}
