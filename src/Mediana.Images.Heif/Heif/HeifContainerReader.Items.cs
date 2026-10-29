// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif.Binary;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Container
{
    internal partial class HeifContainerReader
    {
        private void ReadInformation(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            byte version = Version(reader, 1);
            uint count = version == 0 ? reader.U16() : reader.U32();
            if (count > limits.MaxItems)
            {
                throw new InvalidDataException("Item limit exceeded.");
            }
            HashSet<uint> ids = new HashSet<uint>();
            for (uint i = 0; i < count; i++)
            {
                HeifBox entry = HeifBox.Read(reader, box.Offset);
                if (entry.Type != HeifBox.Code("infe"))
                {
                    throw new InvalidDataException("Expected item information entry.");
                }
                ByteReader info = new ByteReader(entry.Payload);
                byte entryVersion = info.U8();
                ulong flags = info.UInt(3);
                if (flags > 1)
                {
                    throw new UnsupportedHeicFeatureException("item information flags");
                }
                if ((entryVersion != 2) && (entryVersion != 3))
                {
                    throw new UnsupportedHeicFeatureException("item information version");
                }

                uint id = entryVersion == 2 ? info.U16() : info.U32();
                if (!ids.Add(id))
                {
                    throw new InvalidDataException("Duplicate item information.");
                }
                if (info.U16() != 0)
                {
                    throw new UnsupportedHeicFeatureException("protected item");
                }
                HeifItemBuilder item = Item(id);
                item.Type = info.U32();
                item.Hidden = flags == 1;
            }

            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected item information data.");
            }
        }

        private void ReadLocations(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            byte version = Version(reader, 2);
            byte sizes = reader.U8();
            int offsetSize = sizes >> 4;
            int lengthSize = sizes & 15;
            byte baseSizes = reader.U8();
            int baseSize = baseSizes >> 4;
            int indexSize = version == 0 ? 0 : baseSizes & 15;
            uint count = version < 2 ? reader.U16() : reader.U32();
            if (count > limits.MaxItems)
            {
                throw new InvalidDataException("Item limit exceeded.");
            }
            HashSet<uint> ids = new HashSet<uint>();
            for (uint i = 0; i < count; i++)
            {
                uint id = version < 2 ? reader.U16() : reader.U32();
                if (!ids.Add(id))
                {
                    throw new InvalidDataException("Duplicate item location.");
                }
                HeifItemBuilder item = Item(id);
                int method = version == 0 ? 0 : reader.U16();
                if ((method & ~15) != 0)
                {
                    throw new InvalidDataException("Nonzero item location reserved bits.");
                }
                item.ConstructionMethod = method;
                if (reader.U16() != 0)
                {
                    throw new UnsupportedHeicFeatureException("external item data");
                }
                item.BaseOffset = reader.UInt(baseSize);
                int extentCount = reader.U16();
                if (extentCount == 0)
                {
                    throw new InvalidDataException("Item has no data extents.");
                }
                for (int j = 0; j < extentCount; j++)
                {
                    reader.UInt(indexSize);
                    item.Extents.Add(new ItemExtent(reader.UInt(offsetSize), reader.UInt(lengthSize)));
                }
            }

            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected item location data.");
            }
        }

        private void ReadProperties(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            bool propertyContainer = false;
            bool associations = false;
            while (reader.Remaining > 0)
            {
                HeifBox child = HeifBox.Read(reader, box.Offset);
                if (child.Type == HeifBox.Code("ipco"))
                {
                    if (propertyContainer)
                    {
                        throw new InvalidDataException("Duplicate property container.");
                    }
                    propertyContainer = true;
                    ByteReader list = new ByteReader(child.Payload);
                    while (list.Remaining > 0)
                    {
                        if (properties.Count >= (limits.MaxItems * 16L))
                        {
                            throw new InvalidDataException("Property limit exceeded.");
                        }
                        properties.Add(HeifBox.Read(list, child.Offset));
                    }
                }
                else if (child.Type == HeifBox.Code("ipma"))
                {
                    if (associations)
                    {
                        throw new UnsupportedHeicFeatureException("multiple property association boxes");
                    }
                    associations = true;
                    ReadAssociations(child);
                }
            }
        }

        private void ReadAssociations(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            byte version = reader.U8();
            ulong flags = reader.UInt(3);
            if ((version > 1) || (flags > 1))
            {
                throw new UnsupportedHeicFeatureException("property association version or flags");
            }
            uint count = reader.U32();
            if (count > limits.MaxItems)
            {
                throw new InvalidDataException("Item limit exceeded.");
            }
            HashSet<uint> ids = new HashSet<uint>();
            for (uint i = 0; i < count; i++)
            {
                uint id = version == 0 ? reader.U16() : reader.U32();
                if (!ids.Add(id))
                {
                    throw new InvalidDataException("Duplicate item property association entry.");
                }
                HeifItemBuilder item = Item(id);
                int associations = reader.U8();
                for (int j = 0; j < associations; j++)
                {
                    int value = flags == 0 ? reader.U8() : reader.U16();
                    int mask = flags == 0 ? 127 : 32767;
                    if ((value & mask) != 0)
                    {
                        item.Associations.Add(new PropertyAssociation((value & mask) - 1, (value & (mask + 1)) != 0));
                    }
                }
            }

            if (reader.Remaining != 0)
            {
                throw new InvalidDataException("Unexpected property association data.");
            }
        }

        private void ReadReferences(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            byte version = Version(reader, 1);
            while (reader.Remaining > 0)
            {
                HeifBox reference = HeifBox.Read(reader, box.Offset);
                ByteReader data = new ByteReader(reference.Payload);
                uint from = version == 0 ? data.U16() : data.U32();
                int count = data.U16();
                if (count > limits.MaxItems)
                {
                    throw new InvalidDataException("Reference limit exceeded.");
                }
                uint[] to = new uint[count];
                for (int i = 0; i < count; i++)
                {
                    to[i] = version == 0 ? data.U16() : data.U32();
                }
                if (data.Remaining != 0)
                {
                    throw new InvalidDataException("Unexpected item reference data.");
                }
                if (!Item(from).References.TryAdd(reference.Type, to))
                {
                    throw new InvalidDataException("Duplicate item reference type.");
                }
            }
        }
    }
}
