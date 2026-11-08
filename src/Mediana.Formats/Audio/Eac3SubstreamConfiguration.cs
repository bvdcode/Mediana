// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>One independent substream and the declared additional channels of its dependent substreams.</summary>
    public readonly record struct Eac3SubstreamConfiguration(int SampleRate, int BitstreamId, int BitstreamMode,
        int ChannelMode, bool HasLfe, bool AssociatedService, int DependentSubstreamCount, int DependentChannelLocations)
    {
        public int ChannelCount
        {
            get
            {
                int count = Ac3ChannelLayout.GetChannelCount(ChannelMode, HasLfe);
                ReadOnlySpan<int> extra = [2, 2, 1, 1, 2, 2, 2, 1, 1];
                for (int bit = 0; bit < extra.Length; bit++)
                { if ((DependentChannelLocations & 1 << bit) != 0) { count += extra[bit]; } }
                return count;
            }
        }
    }
}
