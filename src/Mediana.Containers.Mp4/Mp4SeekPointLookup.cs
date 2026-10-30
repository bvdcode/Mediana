// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4SeekPointLookup
    {
        public static int? Find(Mp4SampleTable table, Int128 target, int beforeSample, CancellationToken cancellationToken)
        {
            if (beforeSample == 0)
            {
                return null;
            }
            int best = 0;
            int sample = 0;
            int timingRun = 0;
            int compositionRun = 0;
            uint timingRemaining = table.Timing.UInt32(0);
            uint compositionRemaining = table.Composition?.UInt32(0) ?? (uint)table.SampleCount;
            Int128 decodeTime = 0;
            while (sample < beforeSample)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint duration = table.Timing.UInt32(timingRun, 4);
                long composition = table.Composition is null ? 0 : table.SignedComposition
                    ? table.Composition.Int32(compositionRun, 4) : table.Composition.UInt32(compositionRun, 4);
                int count = (int)Math.Min((uint)(beforeSample - sample), Math.Min(timingRemaining, compositionRemaining));
                Int128 firstPresentation = decodeTime + composition;
                if (target >= firstPresentation)
                {
                    int last = sample + count - 1;
                    if (duration != 0)
                    {
                        last = sample + (int)Int128.Min(count - 1, (target - firstPresentation) / duration);
                    }
                    int sync = Mp4SyncSampleLookup.AtOrBefore(table.SyncSamples, last);
                    if (sync >= sample)
                    {
                        best = Math.Max(best, sync);
                    }
                }
                sample += count;
                decodeTime += (Int128)count * duration;
                timingRemaining -= (uint)count;
                compositionRemaining -= (uint)count;
                if (sample < beforeSample)
                {
                    if (timingRemaining == 0)
                    {
                        timingRemaining = table.Timing.UInt32(++timingRun);
                    }
                    if (compositionRemaining == 0 && table.Composition is not null)
                    {
                        compositionRemaining = table.Composition.UInt32(++compositionRun);
                    }
                }
            }
            return best;
        }
    }
}
