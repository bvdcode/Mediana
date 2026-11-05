// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Metadata;
using System.Collections.ObjectModel;

namespace Mediana.Containers
{
    /// <summary>Container header declarations, independent of a reader implementation.
    /// Declared duration is not a scan result or a guarantee of decoded or gapless playback length.</summary>
    public class ContainerInfo
    {
        public ContainerInfo(string formatId, MediaDuration? declaredDuration = null, IEnumerable<MediaTag>? tags = null,
            IEnumerable<ContainerStreamInfo>? streamInfo = null, IEnumerable<MediaDateTimeTag>? dates = null,
            MediaMetadata? metadata = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
            declaredDuration?.TimeBase.Validate();
            Tags = ContainerMetadataSnapshot.CopyTags(tags);
            Dates = ContainerMetadataSnapshot.CopyDates(dates);
            if (streamInfo is not null)
            {
                Dictionary<int, ContainerStreamInfo> snapshot = [];
                foreach (ContainerStreamInfo item in streamInfo)
                {
                    ArgumentNullException.ThrowIfNull(item);
                    if (!snapshot.TryAdd(item.StreamIndex, item))
                    {
                        throw new ArgumentException("Container stream metadata indices must be unique.", nameof(streamInfo));
                    }
                }
                StreamInfo = new ReadOnlyDictionary<int, ContainerStreamInfo>(snapshot);
            }
            FormatId = formatId;
            Metadata = metadata ?? MediaMetadata.Empty;
            DeclaredDuration = declaredDuration;
        }

        /// <summary>Stable container-family identifier, independent of the filename and implementation ID.</summary>
        public string FormatId { get; }
        public MediaMetadata Metadata { get; }
        /// <summary>Duration declared by container metadata in the reader's time representation. Null means unknown.
        /// Format-specific declarations retain native precision when the common representation requires rounding.</summary>
        public MediaDuration? DeclaredDuration { get; }
        /// <summary>Text tags from inspected headers in file order. Null means this reader does not expose
        /// header text tags; an empty list means none were found in the supported inspected metadata.
        /// Later metadata, binary values and unimplemented tag structures are not included.</summary>
        public IReadOnlyList<MediaTag>? Tags { get; }
        /// <summary>Declared calendar dates with native keys and scope. Null means unavailable;
        /// an empty list means no representable, specified dates were found.</summary>
        public IReadOnlyList<MediaDateTimeTag>? Dates { get; }
        /// <summary>Header/index facts keyed by encoded stream index. Null means the reader does not expose track metadata;
        /// a missing entry means facts are unavailable for that stream. Values survive source/reader disposal.</summary>
        public IReadOnlyDictionary<int, ContainerStreamInfo>? StreamInfo { get; }
    }
}
