// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    /// <summary>Explicit source and destination track identities for packet copy or typed encoding.</summary>
    public abstract class MediaTrackRequest
    {
        internal MediaTrackRequest(int inputStreamIndex, int outputStreamIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(inputStreamIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(outputStreamIndex);
            InputStreamIndex = inputStreamIndex;
            OutputStreamIndex = outputStreamIndex;
        }

        public int InputStreamIndex { get; }
        public int OutputStreamIndex { get; }
        internal abstract ValueTask<ProcessedMediaTrack> OpenAsync(MediaEngine engine, MediaTrackInput input, CancellationToken token);
    }
}
