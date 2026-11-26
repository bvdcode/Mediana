// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.IO.Compression;

namespace Mediana.IO
{
    /// <summary>Bounded decoding and prefix inspection of gzip members.</summary>
    public static class GZipInput
    {
        public static bool HasSignature(ReadOnlySpan<byte> source) => source.StartsWith<byte>([0x1f, 0x8b]);

        /// <summary>Reads available uncompressed prefix bytes without requiring a complete member.</summary>
        public static int ReadPrefix(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            int total = 0;
            while (!source.IsEmpty && total < destination.Length)
            {
                using GZipDecoder decoder = new();
                OperationStatus status = decoder.Decompress(source, destination[total..], out int consumed, out int written);
                if (status == OperationStatus.InvalidData) { return 0; }
                total += written;
                if (status != OperationStatus.Done || consumed == 0) { break; }
                source = source[consumed..];
            }
            return total;
        }

        public static byte[] Decode(ReadOnlySpan<byte> source, int maximumBytes, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
            BoundedByteBuffer output = new(maximumBytes);
            Span<byte> overflow = stackalloc byte[1];
            do
            {
                using GZipDecoder decoder = new();
                OperationStatus status;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<byte> space = output.GetMemory().Span;
                    ReadOnlySpan<byte> chunk = source[..Math.Min(source.Length, 16 * 1024)];
                    status = decoder.Decompress(chunk, space.IsEmpty ? overflow : space, out int consumed, out int written);
                    if (status == OperationStatus.InvalidData || (status == OperationStatus.NeedMoreData && consumed == source.Length))
                    {
                        throw new InvalidDataException("Invalid or truncated gzip member.");
                    }
                    if (space.IsEmpty && written > 0) { throw new InvalidDataException("Expanded gzip input exceeds the byte limit."); }
                    if (written > 0) { output.Advance(written); }
                    source = source[consumed..];
                } while (status != OperationStatus.Done);
            } while (!source.IsEmpty);
            return output.Complete(cancellationToken);
        }
    }
}
