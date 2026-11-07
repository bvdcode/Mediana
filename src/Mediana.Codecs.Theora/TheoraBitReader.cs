// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal ref struct TheoraBitReader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> data = source;
        private int offset, remaining, current;
        internal long BitPosition => (long)offset * 8 - remaining;

        internal int Read(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 24);
            int result = 0;
            while (count != 0)
            {
                if (remaining == 0)
                {
                    if (offset == data.Length) { throw new InvalidDataException("The Theora packet is truncated."); }
                    current = data[offset++]; remaining = 8;
                }
                int take = Math.Min(count, remaining); remaining -= take;
                result = (result << take) | ((current >> remaining) & ((1 << take) - 1)); count -= take;
            }
            return result;
        }
        internal bool Flag() => Read(1) != 0;
    }
}
