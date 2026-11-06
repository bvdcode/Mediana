// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal abstract class Mp4FragmentTrack
    {
        public abstract uint TrackId { get; }
        public abstract ulong DecodeTime { get; }
        public abstract int SampleCount { get; }
        public abstract Mp4FragmentSample GetSample(int index, CancellationToken token);
    }
}
