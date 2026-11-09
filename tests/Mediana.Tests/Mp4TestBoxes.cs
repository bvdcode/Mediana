// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Tests
{
    internal static class Mp4TestBoxes
    {
        public static ReadOnlyMemory<byte> Get(ReadOnlyMemory<byte> data, params string[] path)
        {
            foreach (string type in path) { data = Read(data).Single(box => box.Type == type).Data; }
            return data;
        }

        public static uint UInt32(ReadOnlyMemory<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Span[offset..]);
        public static ulong UInt64(ReadOnlyMemory<byte> data, int offset) => BinaryPrimitives.ReadUInt64BigEndian(data.Span[offset..]);

        public static IReadOnlyList<ReadOnlyMemory<byte>> GetAll(ReadOnlyMemory<byte> data, string type) =>
            Read(data).Where(box => box.Type == type).Select(box => box.Data).ToArray();

        private static List<(string Type, ReadOnlyMemory<byte> Data)> Read(ReadOnlyMemory<byte> data)
        {
            List<(string Type, ReadOnlyMemory<byte> Data)> boxes = [];
            int position = 0;
            while (position < data.Length)
            {
                int length = checked((int)UInt32(data, position));
                if (length < 8 || length > data.Length - position)
                {
                    throw new InvalidDataException("The test MP4 box extends beyond its enclosing region.");
                }
                boxes.Add((Encoding.ASCII.GetString(data.Span.Slice(position + 4, 4)), data.Slice(position + 8, length - 8)));
                position += length;
            }
            return boxes;
        }
    }
}
