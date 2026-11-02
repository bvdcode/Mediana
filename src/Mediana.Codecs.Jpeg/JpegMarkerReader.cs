// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Jpeg
{
    internal class JpegMarkerReader(ReadOnlyMemory<byte> encoded, int maximumMarkers, CancellationToken token)
    {
        private int markers;
        internal int Position { get; set; }
        internal ReadOnlyMemory<byte> Encoded { get; } = encoded;

        internal byte NextMarker()
        {
            CountMarker();
            ReadOnlySpan<byte> bytes = Encoded.Span;
            if (Position >= bytes.Length || bytes[Position++] != 255)
            {
                throw new InvalidDataException("JPEG marker prefix is missing.");
            }
            while (Position < bytes.Length && bytes[Position] == 255)
            {
                token.ThrowIfCancellationRequested();
                Position++;
            }
            if (Position >= bytes.Length || bytes[Position] == 0)
            {
                throw new InvalidDataException("JPEG marker code is missing or stuffed outside entropy data.");
            }
            return bytes[Position++];
        }

        internal ReadOnlySpan<byte> ReadSegment()
        {
            ReadOnlySpan<byte> bytes = Encoded.Span;
            if (bytes.Length - Position < 2)
            {
                throw new InvalidDataException("JPEG segment length is missing.");
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[Position..]);
            if (length < 2 || length > bytes.Length - Position)
            {
                throw new InvalidDataException("JPEG segment exceeds the encoded input.");
            }
            ReadOnlySpan<byte> segment = bytes.Slice(Position + 2, length - 2);
            Position += length;
            return segment;
        }

        internal void SkipEntropy()
        {
            ReadOnlySpan<byte> bytes = Encoded.Span;
            while (Position < bytes.Length)
            {
                token.ThrowIfCancellationRequested();
                if (bytes[Position++] != 255)
                {
                    continue;
                }
                int start = Position - 1;
                while (Position < bytes.Length && bytes[Position] == 255)
                {
                    token.ThrowIfCancellationRequested();
                    Position++;
                }
                if (Position >= bytes.Length)
                {
                    break;
                }
                byte code = bytes[Position++];
                if (code == 0)
                {
                    if (Position - start != 2)
                    {
                        throw new InvalidDataException("JPEG entropy byte stuffing is invalid.");
                    }
                    continue;
                }
                if (code is >= 208 and <= 215)
                {
                    CountMarker();
                    continue;
                }
                Position = start;
                return;
            }
            throw new InvalidDataException("JPEG entropy data has no terminating marker.");
        }

        private void CountMarker()
        {
            token.ThrowIfCancellationRequested();
            if (++markers > maximumMarkers)
            {
                throw new InvalidDataException("JPEG exceeds MaximumMarkers.");
            }
        }
    }
}
