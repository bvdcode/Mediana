// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Metadata
{
    /// <summary>Immutable codec header facts, independent of decoded frame storage.</summary>
    public abstract class StreamMetadata
    {
        protected StreamMetadata(MediaKind kind)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            Kind = kind;
        }

        public MediaKind Kind { get; }
    }
}
