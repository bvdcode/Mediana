// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Metadata;

namespace Mediana.Containers.Asf
{
    internal static class AsfTags
    {
        public static void ReadContent(ReadOnlySpan<byte> data, List<MediaTag> tags)
        {
            if (data.Length < 10) { throw new InvalidDataException("Truncated ASF Content Description."); }
            ReadOnlySpan<string> names = ["Title", "Author", "Copyright", "Description", "Rating"];
            int offset = 10;
            for (int index = 0; index < names.Length; index++)
            {
                int length = BinaryPrimitives.ReadUInt16LittleEndian(data[(index * 2)..]);
                string text = ReadText(Take(data, ref offset, length));
                if (text.Length != 0) { tags.Add(new("asf", names[index], text, sourcePath: "Header/Content Description")); }
            }
        }

        public static void ReadExtended(ReadOnlySpan<byte> data, List<MediaTag> tags)
        {
            int offset = 0;
            int count = ReadUInt16(data, ref offset);
            for (int index = 0; index < count; index++)
            {
                int nameLength = ReadUInt16(data, ref offset);
                string name = ReadText(Take(data, ref offset, nameLength));
                int type = ReadUInt16(data, ref offset);
                int length = ReadUInt16(data, ref offset);
                ReadOnlySpan<byte> value = Take(data, ref offset, length);
                if (type == 0 && name.Length != 0)
                {
                    tags.Add(new("asf", name, ReadText(value), sourcePath: "Header/Extended Content Description"));
                }
            }
        }

        private static int ReadUInt16(ReadOnlySpan<byte> data, ref int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(Take(data, ref offset, 2));

        private static string ReadText(ReadOnlySpan<byte> bytes)
        {
            if ((bytes.Length & 1) != 0) { throw new InvalidDataException("ASF text must have a complete UTF-16 code unit."); }
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int offset, int length)
        {
            if (length > data.Length - offset) { throw new InvalidDataException("An ASF tag exceeds its description."); }
            ReadOnlySpan<byte> value = data.Slice(offset, length);
            offset += length;
            return value;
        }
    }
}
