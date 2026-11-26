// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.MpegTs
{
    /// <summary>Reads ISO/IEC 13818-1 transport streams, including fixed 192- and 204-byte packet envelopes.</summary>
    public class MpegTsReaderFactory : IContainerReaderFactory
    {
        public string ImplementationId => "mpegts.managed";
        public int ProbeLength => 409;

        public bool CanRead(ReadOnlySpan<byte> prefix) => Detect(prefix) != 0;

        public async ValueTask<IContainerReader> OpenAsync(IByteSource source, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            IRandomAccessByteSource random = MpegSystemsReader.RequireRandomAccess(source);
            byte[] prefix = new byte[(int)Math.Min(ProbeLength, random.Length!.Value)];
            await random.ReadExactlyAtAsync(0, prefix, cancellationToken).ConfigureAwait(false);
            int stride = Detect(prefix);
            if (stride == 0) { throw new InvalidDataException("MPEG-TS packet spacing could not be recognized."); }
            return await MpegTsDemultiplexer.OpenAsync(random, allocator, stride, stride == 192 ? 4 : 0, cancellationToken).ConfigureAwait(false);
        }

        private static int Detect(ReadOnlySpan<byte> data)
        {
            if (HasSync(data, 0, 188)) { return 188; }
            if (HasSync(data, 4, 192)) { return 192; }
            return HasSync(data, 0, 204) ? 204 : 0;
        }

        private static bool HasSync(ReadOnlySpan<byte> data, int start, int stride) =>
            data.Length > start + 2 * stride && data[start] == 0x47 &&
            data[start + stride] == 0x47 && data[start + 2 * stride] == 0x47;
    }
}
