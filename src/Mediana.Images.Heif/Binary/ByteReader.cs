// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Buffers.Binary;
using System.IO;

namespace Mediana.Images.Heif.Binary
{
    internal class ByteReader(ReadOnlyMemory<byte> data)
    {
        public int Position { get; private set; }
        public int Remaining => data.Length - Position;

        public byte U8()
        {
            Require(1);
            return data.Span[Position++];
        }

        public ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2).Span);
        public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Bytes(4).Span);
        public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Bytes(8).Span);

        public ulong UInt(int bytes)
        {
            if ((bytes < 0) || (bytes > 8))
            {
                throw new InvalidDataException("Invalid integer width.");
            }

            ulong value = 0;
            for (int i = 0; i < bytes; i++)
            {
                value = (value << 8) | U8();
            }

            return value;
        }

        public ReadOnlyMemory<byte> Bytes(int count)
        {
            Require(count);
            ReadOnlyMemory<byte> result = data.Slice(Position, count);
            Position += count;
            return result;
        }

        public void Skip(int count) => Bytes(count);

        private void Require(int count)
        {
            if ((count < 0) || (count > Remaining))
            {
                throw new InvalidDataException("Truncated HEIF field.");
            }
        }
    }
}
