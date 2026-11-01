// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers
{
    internal static class ContainerMetadataSnapshot
    {
        public static IReadOnlyList<MediaTag>? CopyTags(IEnumerable<MediaTag>? tags) => Copy(tags, nameof(tags));

        public static IReadOnlyList<MediaDateTimeTag>? CopyDates(IEnumerable<MediaDateTimeTag>? dates) => Copy(dates, nameof(dates));

        private static IReadOnlyList<T>? Copy<T>(IEnumerable<T>? values, string parameterName) where T : class
        {
            if (values is null)
            {
                return null;
            }
            T[] snapshot = values.ToArray();
            if (snapshot.Any(tag => tag is null))
            {
                throw new ArgumentException("Metadata items cannot contain null.", parameterName);
            }
            return Array.AsReadOnly(snapshot);
        }
    }
}
