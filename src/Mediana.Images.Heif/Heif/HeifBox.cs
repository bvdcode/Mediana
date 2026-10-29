// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Images.Heif.Binary;
using System;
using System.IO;
using System.Text;

namespace Mediana.Images.Heif.Container
{
    internal readonly struct HeifBox(uint type, int offset, ReadOnlyMemory<byte> payload)
    {
        public uint Type { get; } = type;
        public int Offset { get; } = offset;
        public ReadOnlyMemory<byte> Payload { get; } = payload;

        public static HeifBox Read(ByteReader reader, int baseOffset)
        {
            int start = reader.Position;
            ulong size = reader.U32();
            uint type = reader.U32();
            int header = 8;
            if (size == 1)
            {
                size = reader.U64();
                header = 16;
            }
            else if (size == 0)
            {
                size = (ulong)reader.Remaining + (ulong)header;
            }

            if ((size < (ulong)header) || ((size - (ulong)header) > (ulong)reader.Remaining))
            {
                throw new InvalidDataException("Box exceeds its parent extent.");
            }

            return new HeifBox(type, checked(baseOffset + start + header), reader.Bytes((int)size - header));
        }

        public static uint Code(string text)
        {
            if (text.Length != 4)
            {
                throw new ArgumentException("A box type contains four characters.", nameof(text));
            }

            byte[] bytes = Encoding.ASCII.GetBytes(text);
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }
    }
}
