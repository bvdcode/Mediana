// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;

namespace Mediana.Containers.Ogg
{
    internal class OggPage(MediaBuffer data, long offset) : IDisposable
    {
        public const int FixedHeaderBytes = 27;
        public long Offset { get; } = offset;
        public ReadOnlyMemory<byte> Data => data.Memory;
        public byte Flags => Data.Span[5];
        public long Granule => BinaryPrimitives.ReadInt64LittleEndian(Data.Span[6..]);
        public uint Serial => BinaryPrimitives.ReadUInt32LittleEndian(Data.Span[14..]);
        public uint Sequence => BinaryPrimitives.ReadUInt32LittleEndian(Data.Span[18..]);
        public ReadOnlyMemory<byte> Lacing => Data.Slice(FixedHeaderBytes, Data.Span[26]);
        public int BodyOffset => FixedHeaderBytes + Lacing.Length;
        public MediaBuffer Slice(int start, int length) => data.Slice(start, length);
        public void Dispose() => data.Dispose();
    }
}
