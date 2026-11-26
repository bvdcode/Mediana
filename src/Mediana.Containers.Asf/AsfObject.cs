// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.Asf
{
    internal readonly record struct AsfObject(Guid Id, long Start, long End)
    {
        public const int HeaderLength = 24;
        public long PayloadStart => Start + HeaderLength;
        public long PayloadLength => End - PayloadStart;

        public static AsfObject Read(ReadOnlySpan<byte> header, long start, long limit)
        {
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
            if (size < HeaderLength || size > (ulong)(limit - start))
            {
                throw new InvalidDataException("An ASF object exceeds its containing extent.");
            }
            return new(new Guid(header[..16]), start, start + (long)size);
        }
    }
}
