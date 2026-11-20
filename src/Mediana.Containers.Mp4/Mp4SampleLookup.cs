// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4SampleLookup(IMp4SampleIndex table)
    {
        private IMp4SampleCursor cursor = table.CreateCursor();
        private int sample;

        public int? FindSeekSample(Int128 target, int beforeSample, CancellationToken cancellationToken) =>
            table.FindSeekSample(target, beforeSample, cancellationToken);

        public Mp4SampleLocation Find(int index, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, table.SampleCount);
            cancellationToken.ThrowIfCancellationRequested();
            if (index == 0 && sample != 0)
            {
                cursor = table.CreateCursor();
                sample = 0;
            }
            if (index == sample + 1)
            {
                cursor.Advance(cursor.Current);
                sample = index;
            }
            return index == sample ? cursor.Current : table.Locate(index, cancellationToken);
        }
    }
}
