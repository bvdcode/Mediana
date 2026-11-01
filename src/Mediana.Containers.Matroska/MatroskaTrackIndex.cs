// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal class MatroskaTrackIndex(MatroskaTrack track, MatroskaIndexBudget budget)
    {
        public MatroskaTrack Track { get; } = track;
        public MatroskaIndexTable<MatroskaTrackBlock> Blocks { get; } = new(budget);
        public long PacketCount { get; set; }
        public long PayloadBytes { get; set; }
    }
}
