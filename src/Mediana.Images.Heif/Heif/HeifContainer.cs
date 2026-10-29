// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Holds document-owned bytes and a frozen metadata graph safe for concurrent reads.
    /// </summary>
    internal class HeifContainer(ReadOnlyMemory<byte> file, ReadOnlyMemory<byte> itemData,
        uint primaryItemId, Dictionary<uint, HeifItem> items, HeifBox[] properties, int maxItemBytes)
    {
        public uint PrimaryItemId { get; } = primaryItemId;
        public IReadOnlyDictionary<uint, HeifItem> Items { get; } = new ReadOnlyDictionary<uint, HeifItem>(items);
        public IReadOnlyList<HeifBox> Properties { get; } = Array.AsReadOnly(properties);

        public ReadOnlyMemory<byte> ReadData(uint id) => Items[id].ReadData(file, itemData, maxItemBytes);
    }
}
