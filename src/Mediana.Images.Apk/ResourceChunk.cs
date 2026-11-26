// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Images.Apk
{
    internal readonly struct ResourceChunk
    {
        private readonly ReadOnlyMemory<byte> bytes;
        public int HeaderSize { get; }
        public ushort Type => UInt16(0);
        public int Length => bytes.Length;

        public ResourceChunk(ReadOnlyMemory<byte> remaining)
        {
            if (remaining.Length < 8) { throw Invalid(); }
            HeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(remaining.Span[2..]);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(remaining.Span[4..]);
            if (HeaderSize < 8 || length < HeaderSize || length > remaining.Length) { throw Invalid(); }
            bytes = remaining[..(int)length];
        }

        public IEnumerable<ResourceChunk> Children()
        {
            int offset = HeaderSize;
            while (offset < Length)
            {
                ResourceChunk child = new(bytes[offset..]);
                yield return child;
                offset += child.Length;
            }
        }

        public ReadOnlySpan<byte> Slice(int offset, int length)
        {
            if (length < 0 || offset < 0 || offset > bytes.Length - length) { throw Invalid(); }
            return bytes.Span.Slice(offset, length);
        }

        public byte Byte(int offset) => Slice(offset, 1)[0];
        public ushort UInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(offset, 2));
        public uint UInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(offset, 4));
        public int Offset(int offset)
        {
            uint value = UInt32(offset);
            return value <= Length ? (int)value : throw Invalid();
        }

        public static InvalidDataException Invalid() => new("Invalid compiled Android resource.");
    }
}
