// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif.Binary;
using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Container
{
    internal partial class HeifContainerReader(HeicDecodeOptions limits)
    {
        private readonly Dictionary<uint, HeifItemBuilder> items = new Dictionary<uint, HeifItemBuilder>();
        private readonly List<HeifBox> properties = new List<HeifBox>();
        private ReadOnlyMemory<byte> itemData;
        private uint primaryItemId;

        public HeifContainer Read(ReadOnlyMemory<byte> file)
        {
            if (file.Length > limits.MaxInputBytes)
            {
                throw new InvalidDataException("Input byte limit exceeded.");
            }
            ByteReader reader = new ByteReader(file);
            bool fileType = false;
            bool meta = false;
            while (reader.Remaining > 0)
            {
                HeifBox box = HeifBox.Read(reader, 0);
                if (box.Type == HeifBox.Code("ftyp"))
                {
                    if (fileType)
                    {
                        throw new InvalidDataException("Duplicate file type box.");
                    }
                    ReadFileType(box);
                    fileType = true;
                }
                else if (box.Type == HeifBox.Code("meta"))
                {
                    if (meta)
                    {
                        throw new InvalidDataException("Duplicate metadata box.");
                    }
                    ReadMeta(box);
                    meta = true;
                }
            }

            if (!fileType || !meta || !items.ContainsKey(primaryItemId))
            {
                throw new InvalidDataException("HEIF requires a file type, metadata and primary image item.");
            }

            return new HeifContainer(file, itemData, primaryItemId, FreezeItems(), properties.ToArray(), limits.MaxInputBytes);
        }

        private Dictionary<uint, HeifItem> FreezeItems()
        {
            Dictionary<uint, HeifItem> snapshot = new Dictionary<uint, HeifItem>();
            foreach (HeifItemBuilder item in items.Values)
            {
                if (item.Type == 0)
                {
                    throw new InvalidDataException("Item information is missing.");
                }
                foreach (PropertyAssociation association in item.Associations)
                {
                    if (association.Index >= properties.Count)
                    {
                        throw new InvalidDataException("Item property index is out of range.");
                    }
                }

                foreach (uint[] references in item.References.Values)
                {
                    foreach (uint target in references)
                    {
                        if (!items.ContainsKey(target))
                        {
                            throw new InvalidDataException("Item reference target does not exist.");
                        }
                    }
                }

                snapshot.Add(item.Id, item.Build());
            }

            return snapshot;
        }

        private HeifItemBuilder Item(uint id)
        {
            if (id == 0)
            {
                throw new InvalidDataException("Item identifiers must be nonzero.");
            }
            if (!items.TryGetValue(id, out HeifItemBuilder? item))
            {
                if (items.Count >= limits.MaxItems)
                {
                    throw new InvalidDataException("Item limit exceeded.");
                }
                item = new HeifItemBuilder(id);
                items.Add(id, item);
            }

            return item;
        }

        private static void ReadFileType(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            uint major = reader.U32();
            reader.U32();
            bool supported = (major == HeifBox.Code("heic")) || (major == HeifBox.Code("heix"));
            while (reader.Remaining > 0)
            {
                uint brand = reader.U32();
                supported |= (brand == HeifBox.Code("heic")) || (brand == HeifBox.Code("heix"));
            }

            if (!supported)
            {
                throw new UnsupportedHeicFeatureException("file brands without HEVC images");
            }
        }

        private void ReadMeta(HeifBox box)
        {
            ByteReader reader = new ByteReader(box.Payload);
            if (reader.U32() != 0)
            {
                throw new UnsupportedHeicFeatureException("metadata box version or flags");
            }
            HashSet<uint> seen = new HashSet<uint>();
            while (reader.Remaining > 0)
            {
                HeifBox child = HeifBox.Read(reader, box.Offset);
                if (!seen.Add(child.Type))
                {
                    throw new InvalidDataException("Duplicate metadata child box.");
                }
                string type = TypeName(child.Type);
                switch (type)
                {
                    case "pitm":
                        ByteReader primary = new ByteReader(child.Payload);
                        byte version = Version(primary, 1);
                        primaryItemId = version == 0 ? primary.U16() : primary.U32();
                        if (primary.Remaining != 0)
                        {
                            throw new InvalidDataException("Unexpected primary item data.");
                        }
                        break;
                    case "iinf":
                        ReadInformation(child);
                        break;
                    case "iloc":
                        ReadLocations(child);
                        break;
                    case "iprp":
                        ReadProperties(child);
                        break;
                    case "iref":
                        ReadReferences(child);
                        break;
                    case "idat":
                        itemData = child.Payload;
                        break;
                    case "ipro":
                        throw new UnsupportedHeicFeatureException("protected items");
                }
            }
        }

        private static byte Version(ByteReader reader, byte maximum)
        {
            byte version = reader.U8();
            if ((version > maximum) || (reader.UInt(3) != 0))
            {
                throw new UnsupportedHeicFeatureException("box version or flags");
            }

            return version;
        }

        private static string TypeName(uint type) => new string(new[]
        {
            (char)(type >> 24), (char)((type >> 16) & 255), (char)((type >> 8) & 255), (char)(type & 255)
        });
    }
}
