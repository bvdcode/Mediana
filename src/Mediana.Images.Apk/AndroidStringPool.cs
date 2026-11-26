// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;

namespace Mediana.Images.Apk
{
    internal class AndroidStringPool(ResourceChunk chunk)
    {
        private readonly Dictionary<uint, string> strings = [];
        private int decodedCharacters;

        public string Get(uint index)
        {
            if (strings.TryGetValue(index, out string? existing)) { return existing; }
            if (chunk.HeaderSize < 28 || index >= chunk.UInt32(8) || index >= (chunk.Length - chunk.HeaderSize) / 4)
            {
                throw ResourceChunk.Invalid();
            }
            int position = checked(chunk.Offset(20) + chunk.Offset(chunk.HeaderSize + (int)index * 4));
            bool utf8 = (chunk.UInt32(16) & 0x100) != 0;
            string value;
            if (utf8)
            {
                ReadByteLength(ref position);
                int length = ReadByteLength(ref position);
                value = Encoding.UTF8.GetString(chunk.Slice(position, length));
            }
            else
            {
                int length = chunk.UInt16(position);
                position += 2;
                if ((length & 0x8000) != 0)
                {
                    length = ((length & 0x7fff) << 16) | chunk.UInt16(position);
                    position += 2;
                }
                if (length > (chunk.Length - position) / 2) { throw ResourceChunk.Invalid(); }
                value = Encoding.Unicode.GetString(chunk.Slice(position, length * 2));
            }
            decodedCharacters += value.Length;
            if (decodedCharacters > chunk.Length * 2L) { throw ResourceChunk.Invalid(); }
            strings.Add(index, value);
            return value;
        }

        private int ReadByteLength(ref int position)
        {
            int first = chunk.Byte(position++);
            return first < 128 ? first : ((first & 127) << 8) | chunk.Byte(position++);
        }
    }
}
