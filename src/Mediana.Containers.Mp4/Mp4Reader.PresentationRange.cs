// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader
    {
        public MediaTimeRange? GetPresentationRange(int streamIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ValidateIndexedStream(streamIndex);
                cancellationToken.ThrowIfCancellationRequested();
                Mp4TrackInfo track = Tracks[streamIndex];
                if (track.Edits.Count == 0) { return null; }
                _ = Mp4LinearTimeline.Create(track.Stream.TimeBase, track.Edits, 0);
                Mp4Edit edit = track.Edits[^1];
                MediaTimestamp start = timelines[streamIndex].Map(edit.MediaTime);
                TimeBase clock = TimeBase.Common(start.ReduceClock().TimeBase,
                    new MediaTimestamp(edit.Duration.Value, edit.Duration.TimeBase).ReduceClock().TimeBase);
                long position = start.Rescale(clock, TimestampRounding.TowardZero).Value;
                long length = new MediaTimestamp(edit.Duration.Value, edit.Duration.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value;
                return new(new(position, clock), new(checked(position + length), clock));
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
