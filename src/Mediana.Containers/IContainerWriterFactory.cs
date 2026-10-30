// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers
{
    /// <summary>Creates independent muxers. Creation validates stream declarations without writing the destination.</summary>
    public interface IContainerWriterFactory
    {
        string ImplementationId { get; }
        string FormatId { get; }
        IContainerWriter Create(Stream destination, IReadOnlyList<EncodedStreamInfo> streams, MediaBufferAllocator allocator);
    }
}
