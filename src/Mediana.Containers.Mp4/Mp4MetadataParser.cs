// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Containers.Mp4
{
    internal class Mp4MetadataParser(ReadOnlyMemory<byte> root, Mp4ReadOptions options, Mp4ParseBudget budget)
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private static readonly UnicodeEncoding Utf16 = new(true, false, true);
        private readonly List<Mp4MetadataValue> values = [];
        private int textBytes;

        public Mp4MetadataValue[] Parse(Mp4BoxCollection movie)
        {
            Mp4Box? direct = movie.Optional("meta");
            if (direct is Mp4Box directBox)
            {
                ReadMeta(directBox);
            }
            Mp4Box? userData = movie.Optional("udta");
            if (userData is Mp4Box userBox)
            {
                Mp4BoxCollection children = new(userBox.Payload, budget);
                if (children.Optional("meta") is Mp4Box metadata)
                {
                    ReadMeta(metadata);
                }
            }
            return values.ToArray();
        }

        private void ReadMeta(Mp4Box box)
        {
            Mp4Binary.FullBox(box.Payload.Span);
            Mp4BoxCollection children = new(box.Payload[4..], budget);
            Mp4Box handler = children.Required("hdlr");
            Mp4Binary.FullBox(handler.Payload.Span);
            Mp4Binary.Range(handler.Payload.Span, 0, 24);
            string handlerType = Mp4Binary.FourCc(handler.Payload.Span, 8);
            (string Namespace, string Name)[]? keys = handlerType switch
            {
                "mdir" => null,
                "mdta" => ReadKeys(children.Required("keys")),
                _ => throw new NotSupportedException($"MP4 metadata handler '{handlerType}' is not supported.")
            };
            if (children.Optional("ilst") is not Mp4Box list)
            {
                return;
            }
            Mp4BoxCollection items = new(list.Payload, budget);
            foreach (Mp4Box item in items.Boxes)
            {
                Mp4BoxCollection fields = new(item.Payload, budget);
                (string keyNamespace, string name) = ReadKey(item, fields, keys);
                foreach (Mp4Box data in fields.Boxes.Where(field => field.Type == "data"))
                {
                    ReadValue(keyNamespace, name, data.Payload);
                }
            }
        }

        private (string Namespace, string Name)[] ReadKeys(Mp4Box keys)
        {
            Mp4Binary.FullBox(keys.Payload.Span);
            uint count = Mp4Binary.UInt32(keys.Payload.Span, 4);
            if (count > options.MaxMetadataValues)
            {
                throw new NotSupportedException("The MP4 metadata key count exceeds the configured limit.");
            }
            Mp4BoxCollection entries = new(keys.Payload[8..], budget);
            if (count != entries.Boxes.Count)
            {
                throw new InvalidDataException("The MP4 metadata key count does not match its entries.");
            }
            (string Namespace, string Name)[] result = new (string Namespace, string Name)[count];
            for (int index = 0; index < result.Length; index++)
            {
                Mp4Box entry = entries.Boxes[index];
                string name = entry.Type switch
                {
                    "mdta" => ReadText(entry.Payload.Span, Utf8),
                    "udta" when entry.Payload.Length == 4 => Mp4Binary.FourCc(entry.Payload.Span, 0),
                    _ => throw new NotSupportedException($"MP4 metadata key namespace '{entry.Type}' is not supported.")
                };
                if (name.Length == 0)
                {
                    throw new InvalidDataException("An MP4 metadata key cannot be empty.");
                }
                result[index] = (entry.Type, name);
            }
            return result;
        }

        private (string Namespace, string Name) ReadKey(Mp4Box item, Mp4BoxCollection fields,
            (string Namespace, string Name)[]? keys)
        {
            if (keys is not null)
            {
                uint index = ((uint)item.Type[0] << 24) | ((uint)item.Type[1] << 16) | ((uint)item.Type[2] << 8) | item.Type[3];
                if (index == 0 || index > keys.Length)
                {
                    throw new InvalidDataException("An MP4 metadata item has an invalid one-based key index.");
                }
                return keys[index - 1];
            }
            if (item.Type != "----")
            {
                return ("mdir", item.Type);
            }
            return (ReadName(fields.Required("mean")), ReadName(fields.Required("name")));
        }

        private string ReadName(Mp4Box field)
        {
            Mp4Binary.FullBox(field.Payload.Span);
            string value = ReadText(field.Payload.Span[4..], Utf8);
            if (value.Length == 0)
            {
                throw new InvalidDataException("An MP4 freeform metadata name or namespace cannot be empty.");
            }
            return value;
        }

        private void ReadValue(string keyNamespace, string name, ReadOnlyMemory<byte> payload)
        {
            if (values.Count == options.MaxMetadataValues)
            {
                throw new NotSupportedException("The MP4 metadata value count exceeds the configured limit.");
            }
            uint type = Mp4Binary.UInt32(payload.Span, 0);
            uint locale = Mp4Binary.UInt32(payload.Span, 4);
            ReadOnlyMemory<byte> data = payload[8..];
            string? text = type switch
            {
                1 => ReadText(data.Span, Utf8),
                2 => ReadText(data.Span, Utf16),
                _ => null
            };
            int offset = 0;
            if (!data.IsEmpty && (!root.Span.Overlaps(data.Span, out offset) || offset < 0))
            {
                throw new InvalidDataException("The MP4 metadata value lies outside its movie storage.");
            }
            values.Add(new Mp4MetadataValue(new Mp4MetadataItem(keyNamespace, name, type, locale, data.Length, text), offset));
        }

        private string ReadText(ReadOnlySpan<byte> data, Encoding encoding)
        {
            if (data.Length > options.MaxMetadataTextBytes - textBytes)
            {
                throw new NotSupportedException("Decoded MP4 metadata text exceeds the configured byte limit.");
            }
            textBytes += data.Length;
            return encoding.GetString(data);
        }

        public static Mp4CoverArtInfo[] GetCoverArt(Mp4MetadataValue[] metadata)
        {
            List<Mp4CoverArtInfo> covers = [];
            for (int index = 0; index < metadata.Length; index++)
            {
                Mp4MetadataItem item = metadata[index].Item;
                if ((item.Namespace == "mdir" && item.Name == "covr")
                    || (item.Namespace == "mdta" && item.Name == "com.apple.quicktime.artwork"))
                {
                    string? contentType = item.DataType switch
                    {
                        13 => "image/jpeg",
                        14 => "image/png",
                        27 => "image/bmp",
                        _ => null
                    };
                    covers.Add(new Mp4CoverArtInfo(index, item.DataType, item.Locale, item.ByteLength, contentType));
                }
            }
            return covers.ToArray();
        }
    }
}
