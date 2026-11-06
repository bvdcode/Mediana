// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Tiff
{
    internal class TiffDirectory(IReadOnlyDictionary<ushort, TiffField> fields, ulong nextOffset)
    {
        internal IReadOnlyDictionary<ushort, TiffField> Fields { get; } = fields;
        internal ulong NextOffset { get; } = nextOffset;
        internal ulong Number(ushort tag, ulong defaultValue = 0) => Fields.TryGetValue(tag, out TiffField? field) ? field.Unsigned() : defaultValue;
        internal ulong[] Numbers(ushort tag) => Fields.TryGetValue(tag, out TiffField? field)
            ? Enumerable.Range(0, checked((int)field.Count)).Select(field.Unsigned).ToArray() : [];
    }
}
