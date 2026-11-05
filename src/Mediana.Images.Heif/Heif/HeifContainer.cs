// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mediana.Core.Metadata;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Holds encoded byte views and a frozen metadata graph safe for concurrent reads.
    /// The input owner must preserve those bytes for the lifetime of the container.
    /// </summary>
    internal class HeifContainer(ReadOnlyMemory<byte> file, ReadOnlyMemory<byte> itemData,
        uint primaryItemId, Dictionary<uint, HeifItem> items, HeifBox[] properties, int maxItemBytes,
        IEnumerable<MediaProperty>? fileProperties = null)
    {
        public uint PrimaryItemId { get; } = primaryItemId;
        public IReadOnlyDictionary<uint, HeifItem> Items { get; } = new ReadOnlyDictionary<uint, HeifItem>(items);
        public IReadOnlyList<HeifBox> Properties { get; } = Array.AsReadOnly(properties);
        public IReadOnlyList<MediaProperty> FileProperties { get; } = Array.AsReadOnly(fileProperties?.ToArray() ?? []);

        public ReadOnlyMemory<byte> ReadData(uint id) => Items[id].ReadData(file, itemData, maxItemBytes);
    }
}
