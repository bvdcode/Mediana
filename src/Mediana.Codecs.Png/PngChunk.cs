// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats;

namespace Mediana.Codecs.Png
{
    internal static class PngChunk
    {
        public static int Write(Span<byte> destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload,
            CancellationToken cancellationToken)
        {
            BinaryPrimitives.WriteInt32BigEndian(destination, payload.Length);
            type.CopyTo(destination.Slice(4, 4));
            payload.CopyTo(destination.Slice(8, payload.Length));
            Finish(destination, payload.Length, cancellationToken);
            return checked(payload.Length + 12);
        }

        public static void Finish(Span<byte> chunk, int payloadLength, CancellationToken cancellationToken)
        {
            BinaryPrimitives.WriteInt32BigEndian(chunk, payloadLength);
            ReadOnlySpan<byte> data = chunk.Slice(4, checked(payloadLength + 4));
            uint crc = IeeeCrc32.Compute(data, cancellationToken);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.Slice(8 + payloadLength, 4), crc);
        }
    }
}
