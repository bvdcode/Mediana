// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal static class VorbisSetupSections
    {
        public static void ReadFloor(ref VorbisBitReader reader, int books, VorbisSetupVisitor? visitor)
        {
            switch (reader.Read(16))
            {
                case 0:
                    VorbisFloor0Definition floor0 = new((int)reader.Read(8), (int)reader.Read(16),
                        (int)reader.Read(16), (int)reader.Read(6), (int)reader.Read(8));
                    int count = (int)reader.Read(4) + 1;
                    Span<byte> floorBooks = stackalloc byte[16];
                    for (int index = 0; index < count; index++)
                    {
                        uint book = reader.Read(8); RequireBook(book, books); floorBooks[index] = (byte)book;
                    }
                    visitor?.Floor0(floor0, floorBooks[..count]);
                    break;
                case 1:
                    int partitions = (int)reader.Read(5);
                    Span<byte> classes = stackalloc byte[31];
                    int maximumClass = -1;
                    for (int index = 0; index < partitions; index++)
                    {
                        classes[index] = (byte)reader.Read(4);
                        maximumClass = Math.Max(maximumClass, classes[index]);
                    }
                    Span<byte> dimensions = stackalloc byte[16], subclassBits = stackalloc byte[16], masterbooks = stackalloc byte[16];
                    Span<int> subclassBooks = stackalloc int[128];
                    masterbooks.Clear(); subclassBooks.Fill(-1);
                    for (int index = 0; index <= maximumClass; index++)
                    {
                        dimensions[index] = (byte)(reader.Read(3) + 1);
                        int subclasses = (int)reader.Read(2);
                        subclassBits[index] = (byte)subclasses;
                        if (subclasses != 0)
                        {
                            uint book = reader.Read(8); RequireBook(book, books); masterbooks[index] = (byte)book;
                        }
                        for (int book = 0; book < 1 << subclasses; book++)
                        {
                            int reference = (int)reader.Read(8) - 1;
                            if (reference >= books) { throw new InvalidDataException("A Vorbis floor refers to an absent codebook."); }
                            subclassBooks[8 * index + book] = reference;
                        }
                    }
                    int multiplier = (int)reader.Read(2) + 1;
                    int rangeBits = (int)reader.Read(4);
                    Span<ushort> points = stackalloc ushort[250];
                    points[0] = 0;
                    points[1] = (ushort)(1 << rangeBits);
                    int pointCount = 2;
                    for (int index = 0; index < partitions; index++)
                    {
                        for (int point = 0; point < dimensions[classes[index]]; point++)
                        {
                            ushort value = (ushort)reader.Read(rangeBits);
                            if (points[..pointCount].Contains(value)) { throw new InvalidDataException("A Vorbis floor repeats an X coordinate."); }
                            points[pointCount++] = value;
                        }
                    }
                    visitor?.Floor1(new(multiplier, classes[..partitions], dimensions[..(maximumClass + 1)],
                        subclassBits[..(maximumClass + 1)], masterbooks[..(maximumClass + 1)],
                        subclassBooks[..(8 * (maximumClass + 1))], points[..pointCount]));
                    break;
                default:
                    throw new InvalidDataException("A Vorbis floor type is reserved.");
            }
        }

        public static void ReadResidue(ref VorbisBitReader reader, int books, VorbisSetupVisitor? visitor)
        {
            int type = (int)reader.Read(16);
            if (type > 2) { throw new InvalidDataException("A Vorbis residue type is reserved."); }
            uint begin = reader.Read(24), end = reader.Read(24);
            if (end < begin) { throw new InvalidDataException("A Vorbis residue interval is reversed."); }
            int partitionSize = (int)reader.Read(24) + 1;
            int classifications = (int)reader.Read(6) + 1;
            uint classbook = reader.Read(8); RequireBook(classbook, books);
            Span<uint> cascades = stackalloc uint[64];
            Span<int> stageBooks = stackalloc int[512];
            stageBooks.Fill(-1);
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
                    if ((cascades[index] & (1 << pass)) != 0)
                    {
                        uint book = reader.Read(8); RequireBook(book, books); stageBooks[index * 8 + pass] = (int)book;
                    }
                }
            }
            visitor?.Residue(new(type, (int)begin, (int)end, partitionSize, classifications, (int)classbook, stageBooks[..(classifications * 8)]));
        }

        public static void ReadMapping(ref VorbisBitReader reader, int channels, int floors, int residues, VorbisSetupVisitor? visitor)
        {
            if (reader.Read(16) != 0) { throw new InvalidDataException("A Vorbis mapping type is reserved."); }
            int submaps = reader.Read(1) != 0 ? (int)reader.Read(4) + 1 : 1;
            Span<byte> magnitudes = stackalloc byte[256], angles = stackalloc byte[256];
            int steps = 0;
            if (reader.Read(1) != 0)
            {
                steps = (int)reader.Read(8) + 1;
                int bits = VorbisBitReader.ILog((uint)channels - 1);
                for (int index = 0; index < steps; index++)
                {
                    uint magnitude = reader.Read(bits), angle = reader.Read(bits);
                    if (magnitude == angle || magnitude >= channels || angle >= channels)
                    {
                        throw new InvalidDataException("A Vorbis coupling step uses invalid channels.");
                    }
                    magnitudes[index] = (byte)magnitude; angles[index] = (byte)angle;
                }
            }
            if (reader.Read(2) != 0) { throw new InvalidDataException("Vorbis mapping reserved bits are set."); }
            Span<byte> multiplex = stackalloc byte[255]; multiplex[..channels].Clear();
            if (submaps > 1)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    uint submap = reader.Read(4);
                    if (submap >= submaps) { throw new InvalidDataException("A Vorbis channel refers to an absent submap."); }
                    multiplex[channel] = (byte)submap;
                }
            }
            Span<byte> floorIndices = stackalloc byte[16], residueIndices = stackalloc byte[16];
            for (int index = 0; index < submaps; index++)
            {
                reader.Read(8);
                uint floor = reader.Read(8), residue = reader.Read(8);
                if (floor >= floors || residue >= residues)
                {
                    throw new InvalidDataException("A Vorbis submap refers to an absent floor or residue.");
                }
                floorIndices[index] = (byte)floor; residueIndices[index] = (byte)residue;
            }
            visitor?.Mapping(new(multiplex[..channels], floorIndices[..submaps], residueIndices[..submaps], magnitudes[..steps], angles[..steps]));
        }

        private static void RequireBook(uint index, int books)
        {
            if (index >= books) { throw new InvalidDataException("A Vorbis setup section refers to an absent codebook."); }
        }
    }
}
