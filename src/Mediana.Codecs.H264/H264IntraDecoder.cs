// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal static class H264IntraDecoder
    {
        public static H264Picture Decode(ReadOnlySpan<byte> nal, H264Configuration configuration,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            H264BitReader bits = H264Nal.Payload(nal, 5);
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, configuration, nal[0]);
            return H264PictureDecoder.Decode(ref bits, header, [], allocator, cancellationToken);
        }
    }
}
