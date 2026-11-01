// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal class JpegHuffmanTable
    {
        private readonly int[] first = new int[17];
        private readonly int[] counts = new int[17];
        private readonly int[] offsets = new int[17];
        private readonly byte[] symbols;

        internal JpegHuffmanTable(ReadOnlySpan<byte> lengths, ReadOnlySpan<byte> values)
        {
            int code = 0;
            int offset = 0;
            for (int length = 1; length <= 16; length++)
            {
                int count = lengths[length - 1];
                if (code + count >= (1 << length))
                {
                    throw new InvalidDataException("JPEG Huffman table is oversubscribed or uses a reserved all-one code.");
                }
                first[length] = code;
                counts[length] = count;
                offsets[length] = offset;
                offset += count;
                code = (code + count) << 1;
            }
            if (offset == 0 || offset > 256 || offset != values.Length)
            {
                throw new InvalidDataException("JPEG Huffman symbol count is invalid.");
            }
            symbols = values.ToArray();
        }

        internal int Decode(ref JpegEntropyReader bits)
        {
            int code = 0;
            for (int length = 1; length <= 16; length++)
            {
                code = (code << 1) | bits.ReadBits(1);
                int index = code - first[length];
                if ((uint)index < (uint)counts[length])
                {
                    return symbols[offsets[length] + index];
                }
            }
            throw new InvalidDataException("JPEG entropy data contains an undefined Huffman code.");
        }
    }
}
