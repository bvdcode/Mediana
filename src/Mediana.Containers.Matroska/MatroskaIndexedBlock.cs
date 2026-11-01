// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaIndexedBlock(long DataOffset, long EndOffset,
        int ClusterIndex, int StreamIndex, int ExtensionsIndex, short RelativeTimestamp, ushort FrameCount,
        byte Flags, bool IsSimpleBlock, bool HasReferences)
    {
        public EbmlElement Element => new(IsSimpleBlock ? 0xA3U : 0xA1U, DataOffset, EndOffset);
    }
}
