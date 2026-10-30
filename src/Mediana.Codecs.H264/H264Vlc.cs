// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264Vlc
    {
        private readonly int[] branches;
        private readonly Dictionary<int, (ulong Bits, int Length)> codes = [];

        public H264Vlc(string[] codes, int[]? symbols = null)
        {
            List<int> nodes = [0, 0];
            for (int index = 0; index < codes.Length; index++)
            {
                string code = codes[index];
                this.codes.Add(symbols?[index] ?? index, (Convert.ToUInt64(code, 2), code.Length));
                int node = 0;
                for (int bit = 0; bit < code.Length; bit++)
                {
                    int edge = node * 2 + code[bit] - '0';
                    if (bit == code.Length - 1)
                    {
                        if (nodes[edge] != 0)
                        {
                            throw new InvalidOperationException("The H.264 VLC table contains duplicate prefixes.");
                        }
                        nodes[edge] = -(symbols?[index] ?? index) - 1;
                    }
                    else
                    {
                        if (nodes[edge] < 0)
                        {
                            throw new InvalidOperationException("The H.264 VLC table contains overlapping prefixes.");
                        }
                        if (nodes[edge] == 0)
                        {
                            nodes[edge] = nodes.Count / 2;
                            nodes.Add(0);
                            nodes.Add(0);
                        }
                        node = nodes[edge];
                    }
                }
            }
            branches = nodes.ToArray();
        }

        public int Read(ref H264BitReader bits)
        {
            int node = 0;
            while (true)
            {
                int next = branches[node * 2 + bits.ReadBit()];
                if (next == 0)
                {
                    throw new InvalidDataException("An H.264 variable-length code is invalid.");
                }
                if (next < 0)
                {
                    return -next - 1;
                }
                node = next;
            }
        }

        public void Write(H264BitWriter bits, int symbol)
        {
            if (!codes.TryGetValue(symbol, out (ulong Bits, int Length) code))
            {
                throw new ArgumentOutOfRangeException(nameof(symbol));
            }
            bits.WriteBits(code.Bits, code.Length);
        }
    }
}
