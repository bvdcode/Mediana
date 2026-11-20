// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.Mp3
{
    public class Mp3WriterFactory : IContainerWriterFactory
    {
        public string ImplementationId => "mediana.mp3.writer";
        public string FormatId => "mp3";
        public IContainerWriter Create(Stream destination, IReadOnlyList<EncodedStreamInfo> streams, MediaBufferAllocator allocator) =>
            Mp3Writer.Create(destination, streams, allocator);
    }
}
