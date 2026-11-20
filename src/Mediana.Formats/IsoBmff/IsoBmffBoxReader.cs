// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.IsoBmff
{
    /// <summary>Traverses bounded in-memory ISO BMFF boxes without copying payloads or allocating a box list.</summary>
    public ref struct IsoBmffBoxReader(ReadOnlyMemory<byte> data, int baseOffset = 0)
    {
        private int position;
        public readonly int Position => position;
        public readonly ReadOnlyMemory<byte> Remaining => data[position..];

        public bool TryRead(out IsoBmffBox box)
        {
            box = default;
            ReadOnlySpan<byte> bytes = Remaining.Span;
            if (bytes.IsEmpty) { return false; }
            if (bytes.Length < 8) { throw new InvalidDataException("Truncated ISO BMFF box header."); }
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            uint type = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
            int header = 8;
            if (size == 1)
            {
                if (bytes.Length < 16) { throw new InvalidDataException("Truncated ISO BMFF extended size."); }
                size = BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]); header = 16;
            }
            else if (size == 0) { size = (ulong)bytes.Length; }
            bool uuid = type == 0x75756964;
            int payloadStart = header + (uuid ? 16 : 0);
            if (size < (uint)payloadStart || size > (ulong)bytes.Length) { throw new InvalidDataException("ISO BMFF box exceeds its parent extent."); }
            ReadOnlyMemory<byte> extended = uuid ? data.Slice(position + header, 16) : ReadOnlyMemory<byte>.Empty;
            box = new(type, checked(baseOffset + position + payloadStart), data.Slice(position + payloadStart, (int)size - payloadStart), extended);
            position += (int)size;
            return true;
        }
    }
}
