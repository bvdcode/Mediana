// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal class Mp4IndexedFragmentTrack(Mp4Reader reader, Mp4SampleRange range) : Mp4FragmentTrack
    {
        public override uint TrackId => reader.Tracks[range.StreamIndex].TrackId;
        public override ulong DecodeTime => checked((ulong)reader.GetFragmentSample(range.StreamIndex, range.FirstSample, default).DecodeTime);
        public override int SampleCount => range.SampleCount;

        public override Mp4FragmentSample GetSample(int index, CancellationToken token)
        {
            Mp4SampleLocation sample = reader.GetFragmentSample(range.StreamIndex, checked(range.FirstSample + index), token);
            return new(sample.Duration, sample.Size, sample.IsSync, checked(sample.PresentationTime - sample.DecodeTime));
        }
    }
}
