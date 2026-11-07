// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    /// <summary>Writes self-contained initialization and indexed ISO BMFF fragments without processing preceding payloads.
    /// Borrows the reader and destination; callers serialize operations on the same reader. Failed output must be discarded.</summary>
    public static class Mp4FragmentWriter
    {
        public static async ValueTask WriteInitializationAsync(Mp4Reader reader, IReadOnlyList<int> streamIndices,
            Stream destination, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(reader, destination, allocator);
            ArgumentNullException.ThrowIfNull(streamIndices);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer header = reader.CreateFragmentInitialization(streamIndices, allocator, cancellationToken);
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask WriteFragmentAsync(Mp4Reader reader, IReadOnlyList<Mp4SampleRange> ranges,
            uint sequenceNumber, Stream destination, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            Validate(reader, destination, allocator);
            ArgumentNullException.ThrowIfNull(ranges);
            if (ranges.Count == 0 || ranges.Select(range => range.StreamIndex).Distinct().Count() != ranges.Count)
            {
                throw new ArgumentException("A fragment requires distinct nonempty track ranges.", nameof(ranges));
            }
            foreach (Mp4SampleRange range in ranges)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(range.FirstSample);
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(range.SampleCount);
                long count = reader.GetPacketCount(range.StreamIndex);
                if ((long)range.FirstSample + range.SampleCount > count) { throw new ArgumentOutOfRangeException(nameof(ranges)); }
            }
            Mp4FragmentTrack[] tracks = ranges.Select(range => (Mp4FragmentTrack)new Mp4IndexedFragmentTrack(reader, range)).ToArray();
            using MediaBuffer header = Mp4FragmentHeader.Create(tracks, sequenceNumber, allocator, cancellationToken);
            await destination.WriteAsync(header.Memory, cancellationToken).ConfigureAwait(false);
            foreach (Mp4SampleRange range in ranges)
            {
                for (int offset = 0; offset < range.SampleCount; offset++)
                {
                    using MediaPacket packet = await reader.ReadPacketAtAsync(range.StreamIndex, range.FirstSample + offset,
                        cancellationToken).ConfigureAwait(false);
                    await destination.WriteAsync(packet.Data, cancellationToken).ConfigureAwait(false);
                }
            }
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void Validate(Mp4Reader reader, Stream destination, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(allocator);
            if (!destination.CanWrite) { throw new ArgumentException("The fragment destination must be writable.", nameof(destination)); }
        }
    }
}
