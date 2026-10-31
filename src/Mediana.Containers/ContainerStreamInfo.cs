// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Container declarations scoped to one encoded stream. Codec facts remain separate.</summary>
    public class ContainerStreamInfo
    {
        public ContainerStreamInfo(int streamIndex, MediaDuration? declaredDuration = null,
            IEnumerable<MediaTag>? tags = null, ContainerPacketIndexInfo? packetIndex = null, string? language = null,
            IEnumerable<MediaDateTimeTag>? dates = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            declaredDuration?.TimeBase.Validate();
            StreamIndex = streamIndex;
            DeclaredDuration = declaredDuration;
            Tags = ContainerMetadataSnapshot.CopyTags(tags);
            Dates = ContainerMetadataSnapshot.CopyDates(dates);
            PacketIndex = packetIndex;
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
        /// <summary>Complete packet-index totals, or null when not exposed. A zero duration does not define a rate.</summary>
        public ContainerPacketIndexInfo? PacketIndex { get; }
        /// <summary>Declared language identifier (ISO 639 or BCP 47), or null when unavailable.
        /// Original declarations and format-specific selection rules belong to the reader module.</summary>
        public string? Language { get; }
    }
}
