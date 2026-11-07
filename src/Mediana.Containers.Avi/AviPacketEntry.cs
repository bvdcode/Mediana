// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Avi
{
    internal readonly record struct AviPacketEntry(long Offset, long Time, long Duration, int Length, uint ChunkId, bool KeyFrame, long ChunkDataOffset, int ChunkLength, long? PresentationTime = null, long? PresentationDuration = null);
}
