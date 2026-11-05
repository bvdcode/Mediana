// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Immutable metadata scope shared by every media format. Child scopes preserve source relationships
    /// such as streams, pages, images, encoding configurations and native metadata directories.</summary>
    public class MediaMetadata
    {
        public MediaMetadata(string scope, IEnumerable<MediaProperty>? properties = null, IEnumerable<MediaMetadata>? children = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            Scope = scope;
            Properties = Copy(properties);
            Children = Copy(children);
        }

        public static MediaMetadata Empty { get; } = new("media");
        public string Scope { get; }
        public IReadOnlyList<MediaProperty> Properties { get; }
        public IReadOnlyList<MediaMetadata> Children { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values) where T : class
        {
            T[] copy = values?.ToArray() ?? [];
            if (copy.Any(value => value is null))
            {
                throw new ArgumentException("Metadata scopes cannot contain null entries.", nameof(values));
            }
            return Array.AsReadOnly(copy);
        }
    }
}
