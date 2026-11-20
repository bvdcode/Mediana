// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4FragmentCheckpoint(long Offset, long DecodeTime, long MinimumPresentationTime, ulong SyncMask);
}
