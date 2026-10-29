// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO;

namespace Mediana.Images.Heif.Container
{
    /// <summary>
    /// Shares immutable ancestors between independent derived-image branches.
    /// </summary>
    internal class ImageDecodePath
    {
        public ImageDecodePath(uint itemId, ImageDecodePath? parent, int maximumDepth)
        {
            Depth = (parent?.Depth ?? 0) + 1;
            if (Depth > maximumDepth)
            {
                throw new InvalidDataException("Derived image depth limit exceeded.");
            }

            for (ImageDecodePath? ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor.ItemId == itemId)
                {
                    throw new InvalidDataException("Cyclic image derivation or auxiliary reference.");
                }
            }

            ItemId = itemId;
            Parent = parent;
        }

        public uint ItemId { get; }
        public int Depth { get; }
        public ImageDecodePath? Parent { get; }
    }
}
