// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.MpegTs
{
    /// <summary>Reads ISO/IEC 11172-1 and 13818-1 MPEG program streams.</summary>
    public class MpegPsReaderFactory : IContainerReaderFactory
    {
        public string ImplementationId => "mpegps.managed";
        public int ProbeLength => 5;
        public bool CanRead(ReadOnlySpan<byte> prefix) =>
            prefix.Length >= ProbeLength && prefix[..4].SequenceEqual(new byte[] { 0, 0, 1, 0xba }) &&
            ((prefix[4] & 0xc0) == 0x40 || (prefix[4] & 0xf0) == 0x20);

        public ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default) =>
            MpegPsDemultiplexer.OpenAsync(MpegSystemsReader.RequireRandomAccess(source), allocator, cancellationToken);
    }
}
