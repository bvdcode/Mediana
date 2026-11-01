// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Drawing.Fonts
{
    internal ref struct FontGlyphReader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> remaining = data;

        public bool IsEmpty => remaining.IsEmpty;

        public void Require(int length)
        {
            if (length < 0 || length > remaining.Length)
            {
                throw new InvalidDataException("TrueType outline exceeds its glyph data.");
            }
        }

        public ReadOnlySpan<byte> Read(int length)
        {
            Require(length);
            ReadOnlySpan<byte> result = remaining[..length];
            remaining = remaining[length..];
            return result;
        }

        public byte ReadByte() => Read(1)[0];
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Read(sizeof(ushort)));
        public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Read(sizeof(short)));

        public void Skip(int length) => _ = Read(length);
    }
}
