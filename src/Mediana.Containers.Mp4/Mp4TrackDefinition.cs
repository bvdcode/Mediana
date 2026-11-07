// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    /// <summary>Describes one encoded fragment track. Offset ticks shift stored media times; an initialization edit
    /// maps them back to the unchanged presentation timeline. This permits explicit negative codec preroll.</summary>
    public abstract class Mp4TrackDefinition
    {
        internal Mp4TrackDefinition(EncodedStreamInfo stream, uint trackId, long timestampOffset, MediaTimestamp? presentationEnd = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfZero(trackId);
            ArgumentOutOfRangeException.ThrowIfEqual(trackId, uint.MaxValue);
            ArgumentOutOfRangeException.ThrowIfNegative(timestampOffset);
            if (presentationEnd is MediaTimestamp end)
            {
                end.TimeBase.Validate();
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(end.Value);
            }
            Stream = stream;
            TrackId = trackId;
            TimeBase = new(1, stream.TimeBase.Denominator);
            TimestampOffset = timestampOffset;
            PresentationEnd = presentationEnd;
        }

        public EncodedStreamInfo Stream { get; }
        public uint TrackId { get; }
        public TimeBase TimeBase { get; }
        public long TimestampOffset { get; }
        /// <summary>Optional exact movie-timeline endpoint, measured from zero. A finite edit excludes codec tail padding.
        /// Fragment initialization movie and track header durations remain zero.</summary>
        public MediaTimestamp? PresentationEnd { get; }
        internal abstract void WritePresentationSize(Mp4BoxWriter writer);
        internal abstract void WriteMediaHeader(Mp4BoxWriter writer);
        internal abstract void WriteSampleEntry(Mp4BoxWriter writer);
    }
}
