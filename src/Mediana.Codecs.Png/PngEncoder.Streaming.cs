// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    public static partial class PngEncoder
    {
        private const int StreamPayloadBytes = 64 * 1024;

        /// <summary>Encodes straight RGBA32 sRGB pixels directly to a writable, possibly non-seekable stream.
        /// Retains input storage until completion; reserves 64 KiB plus 12 bytes through the supplied allocator.
        /// BCL compression workspace and object overhead are outside that budget. Returns the bytes written.
        /// Does not flush or close the destination. Failure can leave partial output, which must be discarded.
        /// The caller serializes operations on the destination.</summary>
        public static async Task<long> EncodeAsync(VideoFrame input, Stream destination, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            SampleAspectRatio aspectRatio = ValidateInput(input, cancellationToken);
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The PNG destination must be writable.", nameof(destination));
            }
            using VideoFrame source = input.Retain();
            using WritableMediaBuffer scratch = allocator.Allocate(StreamPayloadBytes + 12);
            int headerLength = WriteHeaders(scratch.Memory.Span, source.Format, aspectRatio, cancellationToken);
            await destination.WriteAsync(scratch.Memory[..headerLength], cancellationToken).ConfigureAwait(false);
            using PngIdatWriteStream chunks = new(destination, scratch.Memory, cancellationToken);
            ZLibStream compressor = new(chunks, CompressionLevel.Optimal, leaveOpen: true);
            bool rowsWritten = false;
            try
            {
                ReadOnlyMemory<byte> filter = new byte[1];
                PlaneLayout plane = source.Planes[0];
                for (int row = 0; row < source.Format.Height; row++)
                {
                    await compressor.WriteAsync(filter, cancellationToken).ConfigureAwait(false);
                    ReadOnlyMemory<byte> pixels = source.Data.Slice(checked(plane.Offset + row * plane.Stride), plane.RowBytes);
                    while (!pixels.IsEmpty)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(pixels.Length, 16 * 1024);
                        await compressor.WriteAsync(pixels[..count], cancellationToken).ConfigureAwait(false);
                        pixels = pixels[count..];
                    }
                }
                rowsWritten = true;
            }
            finally
            {
                if (!rowsWritten)
                {
                    chunks.Abort();
                }
                await compressor.DisposeAsync().ConfigureAwait(false);
            }
            await chunks.CompleteAsync().ConfigureAwait(false);
            int endLength = PngChunk.Write(scratch.Memory.Span, "IEND"u8, [], cancellationToken);
            await destination.WriteAsync(scratch.Memory[..endLength], cancellationToken).ConfigureAwait(false);
            return checked(headerLength + chunks.WrittenBytes + endLength);
        }
    }
}
