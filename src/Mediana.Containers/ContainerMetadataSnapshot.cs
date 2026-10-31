// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers
{
    internal static class ContainerMetadataSnapshot
    {
        public static IReadOnlyList<MediaTag>? CopyTags(IEnumerable<MediaTag>? tags)
        {
            if (tags is null)
            {
                return null;
            }
            MediaTag[] snapshot = tags.ToArray();
            if (snapshot.Any(tag => tag is null))
            {
                throw new ArgumentException("Metadata tags cannot contain null.", nameof(tags));
            }
            return Array.AsReadOnly(snapshot);
        }
    }
}
