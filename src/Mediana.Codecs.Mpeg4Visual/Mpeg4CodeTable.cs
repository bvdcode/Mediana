// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class Mpeg4CodeTable
    {
        private readonly ushort[] lookup;
        private readonly int width;
        internal Mpeg4CodeTable(ReadOnlySpan<ushort> codes, ReadOnlySpan<byte> lengths)
        {
            foreach (byte length in lengths) { width = Math.Max(width, length); }
            lookup = new ushort[1 << width];
            for (int symbol = 0; symbol < codes.Length; symbol++)
            {
                int length = lengths[symbol]; if (length == 0) { continue; }
                int start = codes[symbol] << (width - length), end = start + (1 << (width - length));
                for (int i = start; i < end; i++)
                {
                    if (lookup[i] != 0) { throw new InvalidOperationException("The MPEG-4 Visual code table has overlapping entries."); }
                    lookup[i] = (ushort)((length << 8) | (symbol + 1));
                }
            }
        }
        internal int Read(ref Mpeg4VisualBitReader bits)
        {
            int available = (int)Math.Min(width, bits.RemainingBits);
            int entry = lookup[bits.Peek(available) << (width - available)];
            int length = entry >> 8;
            if (entry == 0 || length > available) { throw new InvalidDataException("An MPEG-4 Visual variable-length code is invalid or truncated."); }
            bits.Skip(length); return (entry & 255) - 1;
        }
    }
}
