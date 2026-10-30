// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4SampleLookup(Mp4SampleTable table)
    {
        private Mp4TrackCursor cursor = new(table);
        private int sample;

        public int? FindSeekSample(Int128 target, int beforeSample, CancellationToken cancellationToken) =>
            Mp4SeekPointLookup.Find(table, target, beforeSample, cancellationToken);

        public Mp4SampleLocation Find(int index, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, table.SampleCount);
            cancellationToken.ThrowIfCancellationRequested();
            if (index == 0 && sample != 0)
            {
                cursor = new(table);
                sample = 0;
            }
            if (index == sample + 1)
            {
                cursor.Advance(cursor.Current);
                sample = index;
            }
            return index == sample ? cursor.Current : Mp4SampleLocator.Find(table, index, cancellationToken);
        }
    }
}
