// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal static class VorbisSetupReader
    {
        public static bool[] Read(ref VorbisBitReader reader, int channels, CancellationToken token, VorbisSetupVisitor? visitor = null)
        {
            int books = (int)reader.Read(8) + 1;
            for (int book = 0; book < books; book++)
            {
                token.ThrowIfCancellationRequested();
                ReadBook(ref reader, token, visitor);
            }
            int times = (int)reader.Read(6) + 1;
            for (int index = 0; index < times; index++)
            {
                if (reader.Read(16) != 0) { throw new InvalidDataException("A Vorbis time transform is reserved."); }
            }
            int floors = (int)reader.Read(6) + 1;
            for (int index = 0; index < floors; index++) { VorbisSetupSections.ReadFloor(ref reader, books, visitor); }
            int residues = (int)reader.Read(6) + 1;
            for (int index = 0; index < residues; index++) { VorbisSetupSections.ReadResidue(ref reader, books, visitor); }
            int mappings = (int)reader.Read(6) + 1;
            for (int index = 0; index < mappings; index++) { VorbisSetupSections.ReadMapping(ref reader, channels, floors, residues, visitor); }
            int count = (int)reader.Read(6) + 1;
            bool[] modes = new bool[count];
            for (int index = 0; index < count; index++)
            {
                modes[index] = reader.Read(1) != 0;
                uint window = reader.Read(16), transform = reader.Read(16), mapping = reader.Read(8);
                if (window != 0 || transform != 0 || mapping >= mappings)
                {
                    throw new InvalidDataException("A Vorbis mode uses a reserved window/transform or absent mapping.");
                }
                visitor?.Mode(modes[index], (int)mapping);
            }
            if (reader.Read(1) != 1) { throw new InvalidDataException("The Vorbis setup framing bit is absent."); }
            token.ThrowIfCancellationRequested();
            return modes;
        }

        private static void ReadBook(ref VorbisBitReader reader, CancellationToken token, VorbisSetupVisitor? visitor)
        {
            if (reader.Read(24) != 0x564342) { throw new InvalidDataException("The Vorbis codebook sync pattern is invalid."); }
            int dimensions = (int)reader.Read(16);
            uint entries = reader.Read(24);
            if (dimensions == 0 || entries == 0) { throw new InvalidDataException("A Vorbis codebook has zero dimensions or entries."); }
            Span<byte> captured = visitor is null ? [] : visitor.BeginCodebook(dimensions, (int)entries);
            if (visitor is not null && captured.Length != entries) { throw new InvalidOperationException("The Vorbis codebook length storage differs from its declaration."); }
            Span<uint> lengths = stackalloc uint[33];
            lengths.Clear();
            if (reader.Read(1) != 0)
            {
                int length = (int)reader.Read(5) + 1;
                uint consumed = 0;
                while (consumed < entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (length > 32) { throw new InvalidDataException("A Vorbis codeword exceeds 32 bits."); }
                    uint run = reader.Read(VorbisBitReader.ILog(entries - consumed));
                    if (run > entries - consumed) { throw new InvalidDataException("A Vorbis codeword run exceeds its codebook."); }
                    lengths[length] = run;
                    if (visitor is not null) { captured.Slice((int)consumed, (int)run).Fill((byte)length); }
                    length++;
                    consumed += run;
                }
            }
            else
            {
                bool sparse = reader.Read(1) != 0;
                for (uint entry = 0; entry < entries; entry++)
                {
                    if ((entry & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                    if (!sparse || reader.Read(1) != 0)
                    {
                        int length = (int)reader.Read(5) + 1;
                        lengths[length]++;
                        if (visitor is not null) { captured[(int)entry] = (byte)length; }
                    }
                }
            }
            long available = 1;
            for (int length = 1; length <= 32; length++)
            {
                available = available * 2 - lengths[length];
                if (available < 0) { throw new InvalidDataException("A Vorbis codebook is oversubscribed."); }
            }
            uint lookup = reader.Read(4);
            if (lookup == 0) { visitor?.EndCodebook(token); return; }
            if (lookup > 2) { throw new InvalidDataException("A Vorbis codebook lookup type is reserved."); }
            uint minimum = reader.Read(32), delta = reader.Read(32);
            int valueBits = (int)reader.Read(4) + 1;
            bool sequence = reader.Read(1) != 0;
            long values = lookup switch
            {
                1 => LookupValues(entries, dimensions),
                2 => (long)entries * dimensions,
                _ => throw new InvalidOperationException()
            };
            if (visitor is null) { reader.Skip(values * valueBits); }
            else
            {
                if (values > reader.Remaining / valueBits) { throw new InvalidDataException("The Vorbis codebook lookup is truncated."); }
                Span<ushort> lookupValues = visitor.BeginLookup((int)lookup, minimum, delta, sequence, checked((int)values));
                if (lookupValues.Length != values) { throw new InvalidOperationException("The Vorbis codebook lookup storage differs from its declaration."); }
                for (int index = 0; index < lookupValues.Length; index++)
                {
                    if ((index & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                    lookupValues[index] = (ushort)reader.Read(valueBits);
                }
                visitor.EndCodebook(token);
            }
        }

        private static uint LookupValues(uint entries, int dimensions)
        {
            if (dimensions == 1) { return entries; }
            uint low = 1, high = entries;
            while (low < high)
            {
                uint candidate = low + (high - low + 1) / 2;
                long power = 1;
                for (int dimension = 0; dimension < dimensions && power <= entries; dimension++) { power *= candidate; }
                if (power <= entries) { low = candidate; }
                else { high = candidate - 1; }
            }
            return low;
        }
    }
}
