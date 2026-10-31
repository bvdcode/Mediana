// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal readonly ref struct AacEncodingWindow
    {
        public AacEncodingWindow(int frequencyIndex, int sequence, int shape, scoped ReadOnlySpan<int> groups, Span<int> offsets)
        {
            ShortWindow = sequence switch
            {
                0 or 1 or 3 => false,
                2 => true,
                _ => throw new ArgumentOutOfRangeException(nameof(sequence))
            };
            if (shape is not (0 or 1))
            {
                throw new ArgumentOutOfRangeException(nameof(shape));
            }
            if (groups.Length is < 1 or > 8)
            {
                throw new ArgumentException("AAC window groups require one through eight lengths.", nameof(groups));
            }
            int windows = 0;
            foreach (int length in groups)
            {
                if (length is < 1 or > 8)
                {
                    throw new ArgumentOutOfRangeException(nameof(groups));
                }
                windows += length;
            }
            if (windows != (ShortWindow ? 8 : 1))
            {
                throw new ArgumentException("AAC groups must describe one long window or eight short windows.", nameof(groups));
            }
            ReadOnlySpan<int> table = AacBandTables.Get(frequencyIndex, ShortWindow);
            BandsPerGroup = table.Length - 1;
            BandCount = BandsPerGroup * groups.Length;
            if (offsets.Length < BandCount + 1)
            {
                throw new ArgumentException("AAC grouping requires storage for every spectral band boundary.", nameof(offsets));
            }
            int band = 0;
            int position = 0;
            int firstWindow = 0;
            int grouping = 0;
            offsets[0] = 0;
            foreach (int length in groups)
            {
                for (int localBand = 0; localBand < BandsPerGroup; localBand++)
                {
                    position += (table[localBand + 1] - table[localBand]) * length;
                    offsets[++band] = position;
                }
                for (int window = 1; window < length; window++)
                {
                    grouping |= 1 << (7 - firstWindow - window);
                }
                firstWindow += length;
            }
            Boundaries = offsets[..(BandCount + 1)];
            Sequence = sequence;
            Shape = shape;
            GroupCount = groups.Length;
            Grouping = grouping;
            SectionLengthBits = ShortWindow ? 3 : 5;
        }

        public int Sequence { get; }
        public int Shape { get; }
        public bool ShortWindow { get; }
        public int GroupCount { get; }
        public int Grouping { get; }
        public int BandCount { get; }
        public int BandsPerGroup { get; }
        public int SectionLengthBits { get; }
        public ReadOnlySpan<int> Boundaries { get; }

        public void Group<T>(scoped ReadOnlySpan<T> source, scoped Span<T> destination) => CopyBands(source, destination, true);
        public void Ungroup<T>(scoped ReadOnlySpan<T> source, scoped Span<T> destination) => CopyBands(source, destination, false);

        private void CopyBands<T>(scoped ReadOnlySpan<T> source, scoped Span<T> destination, bool group)
        {
            if (source.Length != 1024 || destination.Length != 1024)
            {
                throw new ArgumentException("AAC spectral grouping requires a complete 1024-coefficient channel.");
            }
            if (!ShortWindow)
            {
                source.CopyTo(destination);
                return;
            }
            if (source.Overlaps(destination))
            {
                throw new ArgumentException("AAC short-window grouping requires nonoverlapping source and destination storage.");
            }
            int firstWindow = 0;
            for (int groupIndex = 0; groupIndex < GroupCount; groupIndex++)
            {
                int firstBand = groupIndex * BandsPerGroup;
                int length = (Boundaries[firstBand + BandsPerGroup] - Boundaries[firstBand]) / 128;
                for (int band = firstBand; band < firstBand + BandsPerGroup; band++)
                {
                    int width = (Boundaries[band + 1] - Boundaries[band]) / length;
                    int offset = (Boundaries[band] - Boundaries[firstBand]) / length;
                    for (int window = 0; window < length; window++)
                    {
                        int packed = Boundaries[band] + window * width;
                        int unpacked = (firstWindow + window) * 128 + offset;
                        if (group)
                        {
                            source.Slice(unpacked, width).CopyTo(destination.Slice(packed, width));
                        }
                        else
                        {
                            source.Slice(packed, width).CopyTo(destination.Slice(unpacked, width));
                        }
                    }
                }
                firstWindow += length;
            }
        }
    }
}
