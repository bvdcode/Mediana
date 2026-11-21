// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.ProRes
{
    internal ref struct ProResBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position;
        public int Remaining => checked(data.Length * 8 - position);
        public bool EndOfData => Remaining <= 31 && PeekRemaining() == 0;

        public uint Read(int count)
        {
            if (count is < 0 or > 32 || count > Remaining)
            {
                throw new InvalidDataException("The ProRes coefficient stream is truncated.");
            }
            uint result = 0;
            while (count > 0)
            {
                int available = 8 - (position & 7);
                int take = Math.Min(count, available);
                result = (result << take) | (uint)((data[position >> 3] >> (available - take)) & ((1 << take) - 1));
                position += take;
                count -= take;
            }
            return result;
        }

        private uint PeekRemaining()
        {
            ProResBitReader copy = this;
            return copy.Read(Remaining);
        }

        public int Symbol(int lastRiceQ, int riceOrder, int expOrder)
        {
            int prefix = 0;
            while (Read(1) == 0)
            {
                if (++prefix > 25)
                {
                    throw new InvalidDataException("The ProRes coefficient code exceeds its numeric range.");
                }
            }
            if (prefix <= lastRiceQ)
            {
                return checked((prefix << riceOrder) + (int)Read(riceOrder));
            }
            int expPrefix = prefix - lastRiceQ - 1;
            int suffixBits = expPrefix + expOrder;
            if (suffixBits > 25)
            {
                throw new InvalidDataException("The ProRes exponential code exceeds its numeric range.");
            }
            return checked((1 << suffixBits) + (int)Read(suffixBits) - (1 << expOrder) + ((lastRiceQ + 1) << riceOrder));
        }
    }
}
