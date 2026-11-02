// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Containers
{
    /// <summary>Container declarations and complete-index facts scoped to one encoded stream. Codec facts remain separate.</summary>
    public class ContainerStreamInfo
    {
        public ContainerStreamInfo(int streamIndex, MediaDuration? declaredDuration = null,
            IEnumerable<MediaTag>? tags = null, ContainerPacketIndexInfo? packetIndex = null, string? language = null,
            IEnumerable<MediaDateTimeTag>? dates = null, FrameRate? averageFrameRate = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            declaredDuration?.TimeBase.Validate();
            averageFrameRate?.Validate();
            StreamIndex = streamIndex;
            DeclaredDuration = declaredDuration;
            Tags = ContainerMetadataSnapshot.CopyTags(tags);
            Dates = ContainerMetadataSnapshot.CopyDates(dates);
            PacketIndex = packetIndex;
            AverageFrameRate = averageFrameRate;
            if (language is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(language);
            }
            Language = language;
        }

        public int StreamIndex { get; }
        /// <summary>Track duration declared by the container in its original time base. Not a scan result or a packet timestamp span.</summary>
        public MediaDuration? DeclaredDuration { get; }
        /// <summary>Native track text tags. Null means unavailable; an empty list means none were found in supported metadata.</summary>
        public IReadOnlyList<MediaTag>? Tags { get; }
        /// <summary>Declared calendar dates scoped to this stream, with native keys and locations.
        /// Null means unavailable; an empty list means no representable, specified dates were found.</summary>
        public IReadOnlyList<MediaDateTimeTag>? Dates { get; }
        /// <summary>Complete packet-index totals, or null when not exposed. Unknown or zero duration does not define a rate.</summary>
        public ContainerPacketIndexInfo? PacketIndex { get; }
        /// <summary>Mean coded-video sample rate on the container's decode timeline, or null when unavailable.
        /// Does not declare constant cadence, edit-list presentation rate or decoded output frame count.</summary>
        public FrameRate? AverageFrameRate { get; }
        /// <summary>Declared language identifier (ISO 639 or BCP 47), or null when unavailable.
        /// Original declarations and format-specific selection rules belong to the reader module.</summary>
        public string? Language { get; }
    }
}
