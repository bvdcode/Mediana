// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal readonly ref struct VorbisFloor1Definition(int multiplier, ReadOnlySpan<byte> partitions,
        ReadOnlySpan<byte> dimensions, ReadOnlySpan<byte> subclasses, ReadOnlySpan<byte> masterbooks,
        ReadOnlySpan<int> subclassBooks, ReadOnlySpan<ushort> points)
    {
        public int Multiplier { get; } = multiplier;
        public ReadOnlySpan<byte> Partitions { get; } = partitions;
        public ReadOnlySpan<byte> Dimensions { get; } = dimensions;
        public ReadOnlySpan<byte> Subclasses { get; } = subclasses;
        public ReadOnlySpan<byte> Masterbooks { get; } = masterbooks;
        public ReadOnlySpan<int> SubclassBooks { get; } = subclassBooks;
        public ReadOnlySpan<ushort> Points { get; } = points;
    }
}
