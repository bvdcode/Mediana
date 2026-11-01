// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.IO
{
    /// <summary>Writes interleaved PCM with explicit byte order, preserving every sample bit.
    /// Borrows the destination, frame and allocator; the frame must remain valid until the operation completes.
    /// Matching byte order writes frame memory directly. Byte swapping uses at most 64 KiB of scratch storage.
    /// Does not flush or close the destination. Failed writes may leave partial output and must be discarded.
    /// The caller serializes operations on the destination.</summary>
    public static class PcmStreamWriter
    {
        private const int MaximumScratchBytes = 64 * 1024;

        public static ValueTask WriteLittleEndianAsync(Stream destination, AudioFrame frame,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            return WriteAsync(destination, frame, allocator, true, cancellationToken);
        }

        public static ValueTask WriteBigEndianAsync(Stream destination, AudioFrame frame,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            return WriteAsync(destination, frame, allocator, false, cancellationToken);
        }

        private static ValueTask WriteAsync(Stream destination, AudioFrame frame,
            MediaBufferAllocator allocator, bool littleEndian, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(frame);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (!destination.CanWrite)
            {
                throw new ArgumentException("The PCM destination must be writable.", nameof(destination));
            }
            if (frame.Format.Layout != AudioStorageLayout.Interleaved)
            {
                throw new NotSupportedException("PCM stream output requires interleaved samples.");
            }
            ReadOnlyMemory<byte> samples = frame.GetPlane(0);
            int sampleBytes = frame.Format.BytesPerSample;
            if (BitConverter.IsLittleEndian == littleEndian || sampleBytes == 1)
            {
                return destination.WriteAsync(samples, cancellationToken);
            }
            return WriteSwappedAsync(destination, samples, sampleBytes, allocator, cancellationToken);
        }

        private static async ValueTask WriteSwappedAsync(Stream destination, ReadOnlyMemory<byte> samples,
            int sampleBytes, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            using WritableMediaBuffer scratch = allocator.Allocate(Math.Min(samples.Length, MaximumScratchBytes));
            int offset = 0;
            while (offset < samples.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = Math.Min(samples.Length - offset, scratch.Memory.Length);
                ReverseSamples(samples.Span.Slice(offset, length), scratch.Memory.Span[..length], sampleBytes);
                await destination.WriteAsync(scratch.Memory[..length], cancellationToken).ConfigureAwait(false);
                offset += length;
            }
        }

        private static void ReverseSamples(ReadOnlySpan<byte> source, Span<byte> destination, int sampleBytes)
        {
            switch (sampleBytes)
            {
                case sizeof(short):
                    BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, short>(source), MemoryMarshal.Cast<byte, short>(destination));
                    break;
                case sizeof(int):
                    BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, int>(source), MemoryMarshal.Cast<byte, int>(destination));
                    break;
                case sizeof(long):
                    BinaryPrimitives.ReverseEndianness(MemoryMarshal.Cast<byte, long>(source), MemoryMarshal.Cast<byte, long>(destination));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sampleBytes));
            }
        }
    }
}
