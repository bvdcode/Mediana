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
            Mp4BoxWriter measure = new(default);
            long payload = BuildHeader(measure, reader, ranges, sequenceNumber, 0, cancellationToken);
            int moofBytes = measure.Position;
            int mdatBytes = payload <= uint.MaxValue - 8 ? 8 : 16;
            using WritableMediaBuffer storage = allocator.Allocate(checked(moofBytes + mdatBytes));
            Mp4BoxWriter writer = new(storage.Memory);
            BuildHeader(writer, reader, ranges, sequenceNumber, checked(moofBytes + mdatBytes), cancellationToken);
            writer.UInt32(mdatBytes == 8 ? checked((uint)(payload + 8)) : 1);
            writer.Bytes("mdat"u8);
            if (mdatBytes == 16) { writer.UInt64(checked((ulong)payload + 16)); }
            await destination.WriteAsync(storage.Memory, cancellationToken).ConfigureAwait(false);
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

        private static long BuildHeader(Mp4BoxWriter writer, Mp4Reader reader, IReadOnlyList<Mp4SampleRange> ranges,
            uint sequence, int payloadOffset, CancellationToken token)
        {
            int moof = writer.Begin("moof"), mfhd = writer.Full("mfhd");
            writer.UInt32(sequence);
            writer.End(mfhd);
            long bytes = 0;
            foreach (Mp4SampleRange range in ranges)
            {
                token.ThrowIfCancellationRequested();
                Mp4SampleLocation first = reader.GetFragmentSample(range.StreamIndex, range.FirstSample, token);
                int traf = writer.Begin("traf"), tfhd = writer.Full("tfhd", 0x020000);
                writer.UInt32(reader.Tracks[range.StreamIndex].TrackId);
                writer.End(tfhd);
                int tfdt = writer.Full("tfdt", 0x01000000);
                writer.UInt64(checked((ulong)first.DecodeTime));
                writer.End(tfdt);
                bool signed = false;
                for (int offset = 0; offset < range.SampleCount; offset++)
                {
                    Mp4SampleLocation sample = reader.GetFragmentSample(range.StreamIndex, range.FirstSample + offset, token);
                    signed |= sample.PresentationTime < sample.DecodeTime;
                }
                int trun = writer.Full("trun", (signed ? 0x01000000u : 0) | 0x000F01);
                writer.UInt32((uint)range.SampleCount);
                writer.UInt32(checked((uint)(int)(payloadOffset + bytes)));
                for (int offset = 0; offset < range.SampleCount; offset++)
                {
                    Mp4SampleLocation sample = reader.GetFragmentSample(range.StreamIndex, range.FirstSample + offset, token);
                    writer.UInt32(sample.Duration);
                    writer.UInt32((uint)sample.Size);
                    writer.UInt32(sample.IsSync ? 0x02000000u : 0x01010000u);
                    long composition = checked(sample.PresentationTime - sample.DecodeTime);
                    writer.UInt32(signed ? unchecked((uint)checked((int)composition)) : checked((uint)composition));
                    bytes = checked(bytes + sample.Size);
                }
                writer.End(trun);
                writer.End(traf);
            }
            writer.End(moof);
            return bytes;
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
