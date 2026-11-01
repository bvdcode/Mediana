// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.MpegTs
{
    public class MpegTsWriterFactory(MpegTsWriteOptions? options = null) : IContainerWriterFactory
    {
        public string ImplementationId => "mediana.mpegts.writer";
        public string FormatId => "mpegts";
        public IContainerWriter Create(Stream destination, IReadOnlyList<EncodedStreamInfo> streams, MediaBufferAllocator allocator) =>
            MpegTsWriter.Create(destination, streams, allocator, options);
    }
}
