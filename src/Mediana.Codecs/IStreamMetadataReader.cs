// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Metadata;

namespace Mediana.Codecs
{
    /// <summary>Reads immutable facts from an encoded stream configuration without I/O or decoder creation.
    /// Implementations must be safe for concurrent calls and retain no media buffers.</summary>
    public interface IStreamMetadataReader
    {
        string ImplementationId { get; }
        string CodecId { get; }
        /// <summary>Returns null for unsupported configuration; malformed syntax throws.
        /// The result kind must match the input. A result does not imply that decoding is supported.</summary>
        StreamMetadata? Read(EncodedStreamInfo stream);
    }
}
