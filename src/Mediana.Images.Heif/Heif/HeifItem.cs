// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Owns immutable item metadata; encoded extents remain views of document storage.
    /// </summary>
    /// <remarks>
    /// A single extent is borrowed without allocation. Multiple extents are concatenated into
    /// decode-call storage, bounded before allocating; neither path mutates document bytes.
    /// </remarks>
    internal class HeifItem(uint id, uint type, bool hidden, int constructionMethod, ulong baseOffset,
        IReadOnlyList<ItemExtent> extents, IReadOnlyList<PropertyAssociation> associations,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>> references)
    {
        public uint Id { get; } = id;
        public uint Type { get; } = type;
        public bool Hidden { get; } = hidden;
        public int ConstructionMethod { get; } = constructionMethod;
        public ulong BaseOffset { get; } = baseOffset;
        public IReadOnlyList<ItemExtent> Extents { get; } = extents;
        public IReadOnlyList<PropertyAssociation> Associations { get; } = associations;
        public IReadOnlyDictionary<uint, IReadOnlyList<uint>> References { get; } = references;

        public ReadOnlyMemory<byte> ReadData(ReadOnlyMemory<byte> file, ReadOnlyMemory<byte> itemData, int maxBytes)
        {
            ReadOnlyMemory<byte> source;
            switch (ConstructionMethod)
            {
                case 0:
                    source = file;
                    break;
                case 1:
                    source = itemData;
                    break;
                default:
                    throw new UnsupportedHeicFeatureException("item construction method " + ConstructionMethod);
            }

            if (Extents.Count == 0)
            {
                throw new InvalidDataException("Item has no data extents.");
            }
            int total = 0;
            foreach (ItemExtent extent in Extents)
            {
                ValidateExtent(source.Length, extent);
                if (extent.Length > (ulong)(maxBytes - total))
                {
                    throw new InvalidDataException("Item byte limit exceeded.");
                }
                total += (int)extent.Length;
            }

            if (Extents.Count == 1)
            {
                return source.Slice(checked((int)(BaseOffset + Extents[0].Offset)), total);
            }

            byte[] result = new byte[total];
            int target = 0;
            foreach (ItemExtent extent in Extents)
            {
                source.Slice((int)(BaseOffset + extent.Offset), (int)extent.Length).CopyTo(result.AsMemory(target));
                target += (int)extent.Length;
            }

            return result;
        }

        private void ValidateExtent(int length, ItemExtent extent)
        {
            if ((BaseOffset > (ulong)length) || (extent.Offset > ((ulong)length - BaseOffset)) ||
                (extent.Length > ((ulong)length - BaseOffset - extent.Offset)))
            {
                throw new InvalidDataException("Item extent exceeds its data source.");
            }
        }
    }
}
