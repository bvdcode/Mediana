// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

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
            if (Remaining.IsEmpty) { return false; }
            IsoBmffBoxHeader header = IsoBmffBoxHeader.Read(Remaining, Remaining.Length);
            int payloadStart = position + header.HeaderLength;
            box = new(header.Type, checked(baseOffset + payloadStart), data.Slice(payloadStart, header.Length - header.HeaderLength), header.ExtendedType);
            position += header.Length;
            return true;
        }
    }
}
