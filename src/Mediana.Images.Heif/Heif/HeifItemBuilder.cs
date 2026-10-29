// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Mediana.Images.Heif.Container
{
    internal class HeifItemBuilder(uint id)
    {
        public uint Id { get; } = id;
        public uint Type { get; set; }
        public bool Hidden { get; set; }
        public int ConstructionMethod { get; set; }
        public ulong BaseOffset { get; set; }
        public List<ItemExtent> Extents { get; } = new List<ItemExtent>();
        public List<PropertyAssociation> Associations { get; } = new List<PropertyAssociation>();
        public Dictionary<uint, uint[]> References { get; } = new Dictionary<uint, uint[]>();

        public HeifItem Build()
        {
            Dictionary<uint, IReadOnlyList<uint>> references = new Dictionary<uint, IReadOnlyList<uint>>();
            foreach (KeyValuePair<uint, uint[]> reference in References)
            {
                references.Add(reference.Key, Array.AsReadOnly((uint[])reference.Value.Clone()));
            }

            return new HeifItem(Id, Type, Hidden, ConstructionMethod, BaseOffset,
                Array.AsReadOnly(Extents.ToArray()), Array.AsReadOnly(Associations.ToArray()),
                new ReadOnlyDictionary<uint, IReadOnlyList<uint>>(references));
        }
    }
}
