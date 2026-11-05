// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Native property identity and ordered typed values. Names are interpreted within their namespace.</summary>
    public class MediaProperty
    {
        public MediaProperty(string keyNamespace, string name, params MediaPropertyValue[] values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyNamespace);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(values);
            if (values.Length == 0 || values.Any(value => value.Kind == MediaPropertyValueKind.Undefined))
            {
                throw new ArgumentException("Metadata properties require defined values.", nameof(values));
            }
            Namespace = keyNamespace;
            Name = name;
            Values = Array.AsReadOnly(values.ToArray());
        }

        public string Namespace { get; }
        public string Name { get; }
        public IReadOnlyList<MediaPropertyValue> Values { get; }
    }
}
