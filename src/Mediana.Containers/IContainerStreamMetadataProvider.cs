// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers
{
    /// <summary>Opening codec parameters established while reading container headers or framing.
    /// These facts do not establish that the encoded payload is valid or supported by a decoder.</summary>
    public interface IContainerStreamMetadataProvider
    {
        /// <summary>Returns an immutable snapshot of established parameters, or null when unavailable.
        /// Does not read the source or move packet cursors. The index must identify an existing stream.</summary>
        StreamMetadata? GetStreamMetadata(int streamIndex);
    }
}
