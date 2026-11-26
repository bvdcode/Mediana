// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Timing;

namespace Mediana.Containers.Asf
{
    internal readonly record struct AsfFileProperties(Guid Id, ulong PacketCount, long PrerollMilliseconds,
        MediaDuration? Duration, int PacketMinimum, int PacketMaximum)
    {
        public static AsfFileProperties Read(ReadOnlySpan<byte> data)
        {
            if (data.Length < 80) { throw new InvalidDataException("Truncated ASF File Properties object."); }
            Guid id = new(data[..16]);
            ulong count = BinaryPrimitives.ReadUInt64LittleEndian(data[32..]);
            ulong duration = BinaryPrimitives.ReadUInt64LittleEndian(data[40..]);
            ulong preroll = BinaryPrimitives.ReadUInt64LittleEndian(data[56..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[64..]);
            uint min = BinaryPrimitives.ReadUInt32LittleEndian(data[68..]);
            uint max = BinaryPrimitives.ReadUInt32LittleEndian(data[72..]);
            if (min == 0 || min > max || max > int.MaxValue || duration > long.MaxValue || preroll > long.MaxValue / 10_000)
            {
                throw new InvalidDataException("Invalid ASF file properties.");
            }
            if ((flags & 1) != 0) { throw new NotSupportedException("ASF broadcast streams do not have a bounded data section."); }
            long presentation = (long)duration - (long)preroll * 10_000;
            return new(id, count, (long)preroll, presentation > 0 ? new(presentation, new(1, 10_000_000)) : null,
                (int)min, (int)max);
        }
    }
}
