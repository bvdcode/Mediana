// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Theora
{
    internal static class TheoraRunDecoder
    {
        internal static void Read(ref TheoraBitReader reader, Span<byte> output, bool longRuns)
        {
            if (output.IsEmpty) { return; }
            int bit = reader.Read(1), index = 0;
            while (index < output.Length)
            {
                int prefix = 0, maximum = longRuns ? 6 : 5;
                while (prefix < maximum && reader.Flag()) { prefix++; }
                int start, bits;
                if (longRuns)
                {
                    (start, bits) = prefix switch
                    { 0 => (1, 0), 1 => (2, 1), 2 => (4, 1), 3 => (6, 2), 4 => (10, 3), 5 => (18, 4), 6 => (34, 12), _ => throw new InvalidDataException() };
                }
                else
                {
                    (start, bits) = prefix switch
                    { 0 => (1, 1), 1 => (3, 1), 2 => (5, 1), 3 => (7, 2), 4 => (11, 2), 5 => (15, 4), _ => throw new InvalidDataException() };
                }
                int length = start + reader.Read(bits);
                if (length > output.Length - index) { throw new InvalidDataException("A Theora run exceeds its block flag list."); }
                output.Slice(index, length).Fill((byte)bit); index += length;
                if (index == output.Length) { return; }
                bit = longRuns && length == 4129 ? reader.Read(1) : 1 - bit;
            }
        }
    }
}
