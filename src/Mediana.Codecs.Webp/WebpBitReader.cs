// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal ref struct WebpBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position, available;
        private ulong bits;
        internal uint Peek(int count)
        {
            while (available < count && position < data.Length) { bits |= (ulong)data[position++] << available; available += 8; }
            return (uint)(bits & ((1u << count) - 1));
        }
        internal void Skip(int count)
        {
            if (count > available) { throw new InvalidDataException("Truncated VP8L bitstream."); }
            bits >>= count; available -= count;
        }
        internal int Read(int count) { uint value = Peek(count); Skip(count); return (int)value; }
    }
}
