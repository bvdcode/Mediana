// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal readonly ref struct VorbisResidueDefinition(int type, int begin, int end, int partitionSize,
        int classifications, int classbook, ReadOnlySpan<int> books)
    {
        public int Type { get; } = type;
        public int Begin { get; } = begin;
        public int End { get; } = end;
        public int PartitionSize { get; } = partitionSize;
        public int Classifications { get; } = classifications;
        public int Classbook { get; } = classbook;
        public ReadOnlySpan<int> Books { get; } = books;
    }
}
