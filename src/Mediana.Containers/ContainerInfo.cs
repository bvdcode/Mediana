// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Metadata;

namespace Mediana.Containers
{
    /// <summary>Container header declarations, independent of a reader implementation.
    /// Declared duration is not a scan result or a guarantee of decoded or gapless playback length.</summary>
    public class ContainerInfo
    {
        public ContainerInfo(string formatId, MediaDuration? declaredDuration = null, IEnumerable<MediaTag>? tags = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
            declaredDuration?.TimeBase.Validate();
            if (tags is not null)
            {
                MediaTag[] snapshot = tags.ToArray();
                if (snapshot.Any(tag => tag is null))
                {
                    throw new ArgumentException("Metadata tags cannot contain null.", nameof(tags));
                }
                Tags = Array.AsReadOnly(snapshot);
            }
            FormatId = formatId;
            DeclaredDuration = declaredDuration;
        }

        /// <summary>Stable container-family identifier, independent of the filename and implementation ID.</summary>
        public string FormatId { get; }
        /// <summary>Duration declared by container metadata, including its original time base. Null means unknown.</summary>
        public MediaDuration? DeclaredDuration { get; }
        /// <summary>Text tags from inspected headers in file order. Null means this reader does not expose
        /// header text tags; an empty list means none were found in the supported inspected metadata.
        /// Later metadata, binary values and unimplemented tag structures are not included.</summary>
        public IReadOnlyList<MediaTag>? Tags { get; }
    }
}
