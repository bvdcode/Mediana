// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Containers.Asf
{
    internal readonly record struct AsfStreamDescription(int Number, int LanguageIndex, uint BitRate, ulong FramePeriod)
    {
        public static AsfStreamDescription Read(ReadOnlySpan<byte> data)
        {
            if (data.Length < 64) { throw new InvalidDataException("Truncated ASF Extended Stream Properties object."); }
            int stream = BinaryPrimitives.ReadUInt16LittleEndian(data[48..]);
            if (stream is < 1 or > 127) { throw new InvalidDataException("Invalid ASF stream number."); }
            return new(stream, BinaryPrimitives.ReadUInt16LittleEndian(data[50..]),
                BinaryPrimitives.ReadUInt32LittleEndian(data[16..]), BinaryPrimitives.ReadUInt64LittleEndian(data[52..]));
        }
    }
}
