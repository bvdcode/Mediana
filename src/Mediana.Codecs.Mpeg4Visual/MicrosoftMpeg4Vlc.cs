// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class MicrosoftMpeg4Vlc
    {
        private const int PrefixBits = 8;
        private readonly int[] prefixes = new int[1 << PrefixBits];
        private readonly int[] tree;

        internal MicrosoftMpeg4Vlc(ReadOnlySpan<uint> entries)
        {
            List<int> nodes = [0, 0];
            for (int symbol = 0; symbol < entries.Length; symbol++)
            {
                uint entry = entries[symbol];
                int length = (int)(entry & 31);
                uint code = entry >> 5;
                int node = 0;
                for (int bit = length - 1; bit >= 0; bit--)
                {
                    int edge = node + (int)((code >> bit) & 1);
                    if (bit == 0)
                    {
                        if (nodes[edge] != 0)
                        {
                            throw new InvalidOperationException("Overlapping Microsoft MPEG-4 codes.");
                        }
                        nodes[edge] = symbol + 1;
                    }
                    else
                    {
                        if (nodes[edge] > 0)
                        {
                            throw new InvalidOperationException("Overlapping Microsoft MPEG-4 codes.");
                        }
                        if (nodes[edge] == 0)
                        {
                            nodes[edge] = -nodes.Count;
                            nodes.Add(0);
                            nodes.Add(0);
                        }
                        node = -nodes[edge];
                    }
                }
            }
            tree = nodes.ToArray();

            for (int prefix = 0; prefix < prefixes.Length; prefix++)
            {
                int branch = 0;
                for (int bit = PrefixBits - 1; bit >= 0; bit--)
                {
                    branch = tree[-branch + ((prefix >> bit) & 1)];
                    if (branch >= 0)
                    {
                        if (branch > 0)
                        {
                            prefixes[prefix] = (branch << 4) | (PrefixBits - bit);
                        }
                        break;
                    }
                    if (bit == 0)
                    {
                        prefixes[prefix] = branch;
                    }
                }
            }
        }

        internal int Read(ref Mpeg4VisualBitReader bits)
        {
            int branch = 0;
            if (bits.RemainingBits >= PrefixBits)
            {
                branch = prefixes[bits.Peek(PrefixBits)];
                if (branch > 0)
                {
                    bits.Skip(branch & 15);
                    return (branch >> 4) - 1;
                }
                if (branch == 0)
                {
                    throw new InvalidDataException("Invalid Microsoft MPEG-4 variable-length code.");
                }
                bits.Skip(PrefixBits);
            }
            do
            {
                branch = tree[-branch + bits.Read(1)];
            }
            while (branch < 0);
            return branch > 0 ? branch - 1 : throw new InvalidDataException("Invalid Microsoft MPEG-4 variable-length code.");
        }
    }
}
