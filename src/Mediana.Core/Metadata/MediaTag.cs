// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.ObjectModel;

namespace Mediana.Core.Metadata
{
    /// <summary>Immutable text from an inspected metadata item. Native namespace, name, value order
    /// and optional qualifiers are preserved; application tag naming and normalization are separate.</summary>
    public class MediaTag
    {
        public MediaTag(string keyNamespace, string name, string value, string? description = null, uint? locale = null, string? sourcePath = null)
            : this(keyNamespace, name, new[] { value }, description, locale, sourcePath)
        {
        }

        public MediaTag(string keyNamespace, string name, IEnumerable<string> values, string? description = null, uint? locale = null, string? sourcePath = null)
            : this(keyNamespace, name, CopyValues(values), description, locale, sourcePath)
        {
        }

        private MediaTag(string keyNamespace, string name, string[] values, string? description, uint? locale, string? sourcePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(keyNamespace);
            ArgumentException.ThrowIfNullOrEmpty(name);
            if (values.Any(value => value is null))
            {
                throw new ArgumentException("Metadata text values cannot contain null.", nameof(values));
            }
            Namespace = keyNamespace;
            Name = name;
            Values = new ReadOnlyCollection<string>(values);
            Description = description;
            Locale = locale;
            SourcePath = sourcePath;
        }

        /// <summary>Native key namespace within its container format, such as id3v2, vorbis, mdir or mdta.</summary>
        public string Namespace { get; }
        public string Name { get; }
        public IReadOnlyList<string> Values { get; }
        /// <summary>Original description, when supplied by the format. Empty and unspecified remain distinct.</summary>
        public string? Description { get; }
        /// <summary>Opaque native locale identifier, when supplied by the format; zero is preserved.</summary>
        public uint? Locale { get; }
        /// <summary>Native structural location supplied by the reader, such as trak/mdia/meta.
        /// Not a filesystem path or a normalized application key. Null means unavailable.</summary>
        public string? SourcePath { get; }

        private static string[] CopyValues(IEnumerable<string> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            return values.ToArray();
        }
    }
}
