// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4SampleLocator
    {
        public static Mp4SampleLocation Find(Mp4SampleTable table, int sample, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sample);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sample, table.SampleCount);
            cancellationToken.ThrowIfCancellationRequested();
            long offset = FindOffset(table, sample, cancellationToken);
            (long decodeTime, uint duration) = FindTiming(table.Timing, sample, cancellationToken);
            long compositionOffset = FindCompositionOffset(table, sample, cancellationToken);
            return new(offset, table.SampleSize(sample), decodeTime, checked(decodeTime + compositionOffset),
                duration, IsSync(table.SyncSamples, sample));
        }

        private static long FindOffset(Mp4SampleTable table, int sample, CancellationToken cancellationToken)
        {
            long remaining = sample;
            for (int run = 0; run < table.ChunkRuns.Count; run++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long firstChunk = table.ChunkRuns.UInt32(run) - 1L;
                long nextChunk = run + 1 < table.ChunkRuns.Count
                    ? table.ChunkRuns.UInt32(run + 1) - 1L : table.ChunkCount;
                uint perChunk = table.ChunkRuns.UInt32(run, 4);
                long count = (nextChunk - firstChunk) * perChunk;
                if (remaining < count)
                {
                    int chunk = checked((int)(firstChunk + remaining / perChunk));
                    int inChunk = (int)(remaining % perChunk);
                    return checked(table.ChunkOffset(chunk) +
                        table.SampleByteCount(sample - inChunk, inChunk, cancellationToken));
                }
                remaining -= count;
            }
            throw new InvalidDataException("The MP4 chunk mapping does not contain the requested sample.");
        }

        private static (long DecodeTime, uint Duration) FindTiming(Mp4Table timing, int sample,
            CancellationToken cancellationToken)
        {
            long decodeTime = 0;
            long remaining = sample;
            for (int run = 0; run < timing.Count; run++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint count = timing.UInt32(run);
                uint duration = timing.UInt32(run, 4);
                if (remaining < count)
                {
                    return (checked(decodeTime + remaining * duration), duration);
                }
                decodeTime = checked(decodeTime + (long)count * duration);
                remaining -= count;
            }
            throw new InvalidDataException("The MP4 timing runs do not contain the requested sample.");
        }

        private static long FindCompositionOffset(Mp4SampleTable table, int sample, CancellationToken cancellationToken)
        {
            if (table.Composition is null)
            {
                return 0;
            }
            long remaining = sample;
            for (int run = 0; run < table.Composition.Count; run++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint count = table.Composition.UInt32(run);
                if (remaining < count)
                {
                    return table.SignedComposition ? table.Composition.Int32(run, 4) : table.Composition.UInt32(run, 4);
                }
                remaining -= count;
            }
            throw new InvalidDataException("The MP4 composition runs do not contain the requested sample.");
        }

        private static bool IsSync(Mp4Table? sync, int sample)
        {
            if (sync is null)
            {
                return true;
            }
            int low = 0;
            int high = sync.Count - 1;
            uint number = (uint)sample + 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                uint candidate = sync.UInt32(middle);
                if (candidate == number)
                {
                    return true;
                }
                if (candidate < number)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return false;
        }
    }
}
