// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal static class VorbisSetupSections
    {
        public static void ReadFloor(ref VorbisBitReader reader, int books)
        {
            switch (reader.Read(16))
            {
                case 0:
                    reader.Skip(8 + 16 + 16 + 6 + 8);
                    int count = (int)reader.Read(4) + 1;
                    for (int index = 0; index < count; index++) { RequireBook(reader.Read(8), books); }
                    break;
                case 1:
                    int partitions = (int)reader.Read(5);
                    Span<int> classes = stackalloc int[31];
                    int maximumClass = -1;
                    for (int index = 0; index < partitions; index++)
                    {
                        classes[index] = (int)reader.Read(4);
                        maximumClass = Math.Max(maximumClass, classes[index]);
                    }
                    Span<int> dimensions = stackalloc int[16];
                    for (int index = 0; index <= maximumClass; index++)
                    {
                        dimensions[index] = (int)reader.Read(3) + 1;
                        int subclasses = (int)reader.Read(2);
                        if (subclasses != 0) { RequireBook(reader.Read(8), books); }
                        for (int book = 0; book < 1 << subclasses; book++)
                        {
                            int reference = (int)reader.Read(8) - 1;
                            if (reference >= books) { throw new InvalidDataException("A Vorbis floor refers to an absent codebook."); }
                        }
                    }
                    reader.Read(2);
                    int rangeBits = (int)reader.Read(4);
                    Span<uint> points = stackalloc uint[250];
                    points[0] = 0;
                    points[1] = 1u << rangeBits;
                    int pointCount = 2;
                    for (int index = 0; index < partitions; index++)
                    {
                        for (int point = 0; point < dimensions[classes[index]]; point++)
                        {
                            uint value = reader.Read(rangeBits);
                            if (points[..pointCount].Contains(value)) { throw new InvalidDataException("A Vorbis floor repeats an X coordinate."); }
                            points[pointCount++] = value;
                        }
                    }
                    break;
                default:
                    throw new InvalidDataException("A Vorbis floor type is reserved.");
            }
        }

        public static void ReadResidue(ref VorbisBitReader reader, int books)
        {
            if (reader.Read(16) > 2) { throw new InvalidDataException("A Vorbis residue type is reserved."); }
            uint begin = reader.Read(24), end = reader.Read(24);
            if (end < begin) { throw new InvalidDataException("A Vorbis residue interval is reversed."); }
            reader.Read(24);
            int classifications = (int)reader.Read(6) + 1;
            RequireBook(reader.Read(8), books);
            Span<uint> cascades = stackalloc uint[64];
            for (int index = 0; index < classifications; index++)
            {
                uint low = reader.Read(3);
                uint high = reader.Read(1) != 0 ? reader.Read(5) : 0;
                cascades[index] = (high << 3) | low;
            }
            for (int index = 0; index < classifications; index++)
            {
                for (int pass = 0; pass < 8; pass++)
                {
                    if ((cascades[index] & (1 << pass)) != 0) { RequireBook(reader.Read(8), books); }
                }
            }
        }

        public static void ReadMapping(ref VorbisBitReader reader, int channels, int floors, int residues)
        {
            if (reader.Read(16) != 0) { throw new InvalidDataException("A Vorbis mapping type is reserved."); }
            int submaps = reader.Read(1) != 0 ? (int)reader.Read(4) + 1 : 1;
            if (reader.Read(1) != 0)
            {
                int steps = (int)reader.Read(8) + 1;
                int bits = VorbisBitReader.ILog((uint)channels - 1);
                for (int index = 0; index < steps; index++)
                {
                    uint magnitude = reader.Read(bits), angle = reader.Read(bits);
                    if (magnitude == angle || magnitude >= channels || angle >= channels)
                    {
                        throw new InvalidDataException("A Vorbis coupling step uses invalid channels.");
                    }
                }
            }
            if (reader.Read(2) != 0) { throw new InvalidDataException("Vorbis mapping reserved bits are set."); }
            if (submaps > 1)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    if (reader.Read(4) >= submaps) { throw new InvalidDataException("A Vorbis channel refers to an absent submap."); }
                }
            }
            for (int index = 0; index < submaps; index++)
            {
                reader.Read(8);
                if (reader.Read(8) >= floors || reader.Read(8) >= residues)
                {
                    throw new InvalidDataException("A Vorbis submap refers to an absent floor or residue.");
                }
            }
        }

        private static void RequireBook(uint index, int books)
        {
            if (index >= books) { throw new InvalidDataException("A Vorbis setup section refers to an absent codebook."); }
        }
    }
}
