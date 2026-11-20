// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4FragmentCursor : IMp4SampleCursor
    {
        private readonly IMp4SampleCursor initial;
        private readonly int initialCount;
        private readonly uint initialLastExtension;
        private readonly IReadOnlyList<Mp4FragmentRun> runs;
        private int initialSample;
        private int runIndex;
        private int runSample;
        private Mp4SampleLocation current;

        public Mp4FragmentCursor(IMp4SampleIndex initial, uint initialLastExtension, IReadOnlyList<Mp4FragmentRun> runs)
        {
            this.initial = initial.CreateCursor();
            initialCount = initial.SampleCount;
            this.initialLastExtension = initialLastExtension;
            this.runs = runs;
            if (!this.initial.HasSample && runs.Count != 0)
            {
                current = runs[0].Locate(0);
            }
        }

        public bool HasSample => initial.HasSample || runIndex < runs.Count;

        public Mp4SampleLocation Current
        {
            get
            {
                if (initial.HasSample)
                {
                    Mp4SampleLocation sample = initial.Current;
                    return initialSample == initialCount - 1
                        ? sample with { Duration = checked(sample.Duration + initialLastExtension) } : sample;
                }
                return current;
            }
        }

        public void Advance(Mp4SampleLocation previous)
        {
            if (initial.HasSample)
            {
                initial.Advance(initial.Current);
                initialSample++;
                if (!initial.HasSample && runs.Count != 0)
                {
                    current = runs[0].Locate(0);
                }
                return;
            }
            Mp4FragmentRun run = runs[runIndex];
            if (++runSample < run.SampleCount)
            {
                current = run.Read(runSample, checked(previous.Offset + previous.Size), checked(previous.DecodeTime + previous.Duration));
                return;
            }
            runSample = 0;
            if (++runIndex < runs.Count)
            {
                current = runs[runIndex].Locate(0);
            }
        }
    }
}
