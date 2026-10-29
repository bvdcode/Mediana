// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4TrackCursor
    {
        private readonly Mp4SampleTable table;
        private int sample;
        private int chunk;
        private int chunkRun;
        private uint samplesInChunk;
        private long offset;
        private long decodeTime;
        private int timingRun;
        private uint timingRemaining;
        private int compositionRun;
        private uint compositionRemaining;
        private int syncIndex;

        public Mp4TrackCursor(Mp4SampleTable table)
        {
            this.table = table;
            if (table.SampleCount > 0)
            {
                offset = table.ChunkOffset(0);
                samplesInChunk = table.ChunkRuns.UInt32(0, 4);
                timingRemaining = table.Timing.UInt32(0);
                compositionRemaining = table.Composition?.UInt32(0) ?? 0;
            }
        }

        public bool HasSample => sample < table.SampleCount;

        public Mp4SampleLocation Current
        {
            get
            {
                long composition = 0;
                if (table.Composition is not null)
                {
                    composition = table.SignedComposition
                        ? table.Composition.Int32(compositionRun, 4) : table.Composition.UInt32(compositionRun, 4);
                }
                bool sync = table.SyncSamples is null ||
                    (syncIndex < table.SyncSamples.Count && table.SyncSamples.UInt32(syncIndex) == sample + 1);
                return new Mp4SampleLocation(offset, table.SampleSize(sample), decodeTime,
                    checked(decodeTime + composition), table.Timing.UInt32(timingRun, 4), sync);
            }
        }

        public void Advance(Mp4SampleLocation current)
        {
            sample++;
            if (table.SyncSamples is not null && current.IsSync)
            {
                syncIndex++;
            }
            if (!HasSample)
            {
                return;
            }
            decodeTime += current.Duration;
            offset += current.Size;
            samplesInChunk--;
            if (samplesInChunk == 0)
            {
                chunk++;
                if (chunkRun + 1 < table.ChunkRuns.Count && table.ChunkRuns.UInt32(chunkRun + 1) == chunk + 1)
                {
                    chunkRun++;
                }
                offset = table.ChunkOffset(chunk);
                samplesInChunk = table.ChunkRuns.UInt32(chunkRun, 4);
            }
            timingRemaining--;
            if (timingRemaining == 0)
            {
                timingRun++;
                timingRemaining = table.Timing.UInt32(timingRun);
            }
            if (table.Composition is not null)
            {
                compositionRemaining--;
                if (compositionRemaining == 0)
                {
                    compositionRun++;
                    compositionRemaining = table.Composition.UInt32(compositionRun);
                }
            }
        }
    }
}
