// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.IsoBmff
{
    internal readonly record struct IsoBmffBoxHeader(uint Type, int Length, int HeaderLength, ReadOnlyMemory<byte> ExtendedType)
    {
        internal static int RequiredBytes(ReadOnlySpan<byte> prefix)
        {
            if (prefix.Length < 8) { throw new InvalidDataException("Truncated ISO BMFF box header."); }
            int sizeBytes = BinaryPrimitives.ReadUInt32BigEndian(prefix) == 1 ? 16 : 8;
            return prefix[4..8].SequenceEqual("uuid"u8) ? sizeBytes + 16 : sizeBytes;
        }

        internal static IsoBmffBoxHeader Read(ReadOnlyMemory<byte> prefix, int remaining)
        {
            int header = RequiredBytes(prefix.Span);
            if (header > remaining || header > prefix.Length) { throw new InvalidDataException("Truncated ISO BMFF box header."); }
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(prefix.Span) switch
            {
                0 => (ulong)remaining,
                1 => BinaryPrimitives.ReadUInt64BigEndian(prefix.Span[8..]),
                uint normal => normal
            };
            if (size < (uint)header || size > (ulong)remaining) { throw new InvalidDataException("ISO BMFF box exceeds its parent extent."); }
            uint type = BinaryPrimitives.ReadUInt32BigEndian(prefix.Span[4..]);
            ReadOnlyMemory<byte> extended = type == 0x75756964 ? prefix.Slice(header - 16, 16) : default;
            return new(type, (int)size, header, extended);
        }
    }
}
