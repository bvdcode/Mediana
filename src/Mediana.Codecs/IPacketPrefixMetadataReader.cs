// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Codecs
{
    /// <summary>Reads opening stream facts from a bounded packet prefix when configuration metadata is unavailable.
    /// Has the concurrency, ownership and validation requirements of <see cref="IStreamMetadataReader"/>.</summary>
    public interface IPacketPrefixMetadataReader : IStreamMetadataReader
    {
        int PacketProbeLength { get; }

        /// <summary>Inspects the opening packet prefix without reconstructing pictures or PCM.
        /// The prefix may be shorter than requested when the complete packet is shorter.</summary>
        StreamMetadata? ReadPacketPrefix(EncodedStreamInfo stream, ReadOnlySpan<byte> prefix);
    }
}
