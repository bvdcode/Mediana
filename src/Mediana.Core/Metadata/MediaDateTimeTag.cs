// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Immutable calendar date from a native metadata field, independent of packet time.
    /// UTC is retained when declared; Unspecified means no reliable time zone is available.</summary>
    public class MediaDateTimeTag
    {
        public MediaDateTimeTag(string keyNamespace, string name, DateTime value, string? sourcePath = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(keyNamespace);
            ArgumentException.ThrowIfNullOrEmpty(name);
            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                case DateTimeKind.Unspecified:
                    break;
                case DateTimeKind.Local:
                default:
                    throw new ArgumentException("Media calendar dates cannot depend on the machine's local time zone.", nameof(value));
            }
            Namespace = keyNamespace;
            Name = name;
            Value = value;
            SourcePath = sourcePath;
        }

        public string Namespace { get; }
        public string Name { get; }
        public DateTime Value { get; }
        /// <summary>Native structural location, independent of filesystem paths or normalized application keys.</summary>
        public string? SourcePath { get; }
    }
}
