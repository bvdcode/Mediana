// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.IO.Compression;

namespace Mediana.IO
{
    /// <summary>Decodes one zlib member into an independently owned, bounded array.
    /// Validates the member's end and checksum; bytes after that member are ignored.
    /// Temporary segments reserve at most the limit; the returned array is additional storage.</summary>
    public static class BoundedZLibDecoder
    {
        private const int StepBytes = 16 * 1024;

        public static byte[] Decode(ReadOnlySpan<byte> source, int maximumBytes, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
            cancellationToken.ThrowIfCancellationRequested();
            using ZLibDecoder decoder = new();
            BoundedByteBuffer buffer = new(maximumBytes);
            Span<byte> overflow = stackalloc byte[1];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<byte> output = buffer.GetMemory().Span;
                bool atLimit = output.IsEmpty;
                Span<byte> destination = atLimit ? overflow : output[..Math.Min(output.Length, StepBytes)];
                OperationStatus status = decoder.Decompress(source[..Math.Min(source.Length, StepBytes)], destination,
                    out int consumed, out int written);
                source = source[consumed..];
                if (atLimit && written != 0)
                {
                    throw new InvalidDataException("The zlib stream exceeds the configured byte limit.");
                }
                if (!atLimit) { buffer.Advance(written); }
                switch (status)
                {
                    case OperationStatus.Done:
                        return buffer.Complete(cancellationToken);
                    case OperationStatus.NeedMoreData:
                        if (source.IsEmpty) { throw new InvalidDataException("Truncated zlib stream."); }
                        break;
                    case OperationStatus.DestinationTooSmall:
                        break;
                    case OperationStatus.InvalidData:
                        throw new InvalidDataException("Invalid zlib stream or checksum.");
                    default:
                        throw new InvalidOperationException("Unknown zlib decoder status.");
                }
            }
        }
    }
}
