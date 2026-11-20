// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Images.Metadata
{
    internal ref struct ImageMetadataCursor(ReadOnlyMemory<byte> data)
    {
        private ReadOnlyMemory<byte> remaining = data;
        internal int Remaining => remaining.Length;
        internal byte Byte() => Take(1).Span[0];
        internal ushort Word() => BinaryPrimitives.ReadUInt16BigEndian(Take(2).Span);
        internal uint Dword() => BinaryPrimitives.ReadUInt32BigEndian(Take(4).Span);
        internal ReadOnlyMemory<byte> Take(ulong count)
        {
            if (count > (ulong)remaining.Length) { throw new InvalidDataException("Image metadata extends beyond its input."); }
            ReadOnlyMemory<byte> result = remaining[..(int)count];
            remaining = remaining[(int)count..];
            return result;
        }
    }
}
