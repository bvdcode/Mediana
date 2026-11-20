// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4FragmentSampleIndex(IMp4SampleIndex initial) : IMp4SampleIndex
    {
        private readonly List<Mp4FragmentRun> runs = [];
        private uint initialLastExtension;
        private long? compositionOffset;

        public int SampleCount { get; private set; } = initial.SampleCount;
        public long PayloadBytes { get; private set; } = initial.PayloadBytes;
        public long DecodeDurationTicks { get; private set; } = initial.DecodeDurationTicks;
        public long DecodeTimeLeadTicks { get; private set; } = initial.DecodeTimeLeadTicks;
        public long EndDecodeTime { get; private set; } = initial.DecodeDurationTicks;
        public bool RequiresPresentationTiming { get; private set; } = initial.RequiresPresentationTiming;

        public void StartFragment(long decodeTime, CancellationToken token)
        {
            if (decodeTime < EndDecodeTime)
            {
                throw new InvalidDataException("The MP4 fragment decode timeline overlaps preceding samples.");
            }
            long extension = decodeTime - EndDecodeTime;
            if (extension != 0 && SampleCount != 0)
            {
                if (runs.Count != 0)
                {
                    runs[^1].ExtendLastDuration(extension);
                }
                else
                {
                    uint duration = initial.Locate(initial.SampleCount - 1, token).Duration;
                    initialLastExtension = checked(initialLastExtension + (uint)extension);
                    _ = checked(duration + initialLastExtension);
                }
                DecodeDurationTicks = checked(DecodeDurationTicks + extension);
            }
            EndDecodeTime = decodeTime;
        }

        public void Add(Mp4FragmentRun run, CancellationToken token)
        {
            if (run.SampleCount == 0)
            {
                return;
            }
            if (!RequiresPresentationTiming)
            {
                if (compositionOffset is null && initial.SampleCount != 0)
                {
                    Mp4SampleLocation first = initial.Locate(0, token);
                    compositionOffset = checked(first.PresentationTime - first.DecodeTime);
                }
                compositionOffset ??= run.CompositionOffset;
                RequiresPresentationTiming = run.HasVaryingComposition || run.CompositionOffset != compositionOffset;
            }
            runs.Add(run);
            SampleCount = checked(SampleCount + run.SampleCount);
            PayloadBytes = checked(PayloadBytes + run.PayloadBytes);
            DecodeDurationTicks = checked(DecodeDurationTicks + run.DecodeDurationTicks);
            DecodeTimeLeadTicks = Math.Max(DecodeTimeLeadTicks, run.DecodeTimeLeadTicks);
            EndDecodeTime = run.EndDecodeTime;
        }

        public void AddEmptyDuration(uint duration) => EndDecodeTime = checked(EndDecodeTime + duration);

        public IMp4SampleCursor CreateCursor() => new Mp4FragmentCursor(initial, initialLastExtension, runs);

        public Mp4SampleLocation Locate(int sample, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sample < initial.SampleCount)
            {
                Mp4SampleLocation location = initial.Locate(sample, cancellationToken);
                return sample == initial.SampleCount - 1
                    ? location with { Duration = checked(location.Duration + initialLastExtension) } : location;
            }
            Mp4FragmentRun run = runs[FindRun(sample)];
            return run.Locate(sample - run.FirstSample);
        }

        public int FindSyncSample(int sample)
        {
            int runIndex = FindRun(sample);
            while (runIndex >= 0)
            {
                Mp4FragmentRun run = runs[runIndex];
                int sync = run.FindSyncSample(Math.Min(sample - run.FirstSample, run.SampleCount - 1));
                if (sync >= 0)
                {
                    return run.FirstSample + sync;
                }
                runIndex--;
            }
            return initial.SampleCount == 0 ? -1 : initial.FindSyncSample(Math.Min(sample, initial.SampleCount - 1));
        }

        public int? FindSeekSample(Int128 target, int beforeSample, CancellationToken cancellationToken)
        {
            if (beforeSample == 0)
            {
                return null;
            }
            for (int index = FindRun(beforeSample - 1); index >= 0; index--)
            {
                Mp4FragmentRun run = runs[index];
                int sample = run.FindSeekSample(target, Math.Min(beforeSample - run.FirstSample, run.SampleCount), cancellationToken);
                if (sample >= 0)
                {
                    return run.FirstSample + sample;
                }
            }
            return initial.FindSeekSample(target, Math.Min(beforeSample, initial.SampleCount), cancellationToken) ?? 0;
        }

        public long GetPresentationEnd(CancellationToken cancellationToken)
        {
            long end = initial.GetPresentationEnd(cancellationToken);
            if (initial.SampleCount != 0 && initialLastExtension != 0)
            {
                Mp4SampleLocation last = Locate(initial.SampleCount - 1, cancellationToken);
                end = Math.Max(end, checked(last.PresentationTime + last.Duration));
            }
            foreach (Mp4FragmentRun run in runs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                end = Math.Max(end, run.PresentationEnd);
            }
            return end;
        }

        private int FindRun(int sample)
        {
            int low = 0;
            int high = runs.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (runs[middle].FirstSample <= sample)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low - 1;
        }
    }
}
