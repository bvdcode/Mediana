// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Containers.Matroska
{
    /// <summary>Allocator-owned displayed-picture metadata. Implicit display extents reach the next visible PTS;
    /// explicit BlockDuration preserves breaks. Stored packet timestamps and durations remain unchanged.</summary>
    internal class MatroskaPresentationIndex : IDisposable
    {
        private readonly WritableMediaBuffer?[] storage;
        private readonly int[] counts;

        private MatroskaPresentationIndex(int tracks)
        {
            storage = new WritableMediaBuffer?[tracks];
            counts = new int[tracks];
        }

        public static async ValueTask<MatroskaPresentationIndex> CreateAsync(IRandomAccessByteSource source,
            MediaBufferAllocator allocator, MatroskaPacketIndex index, MatroskaPacketSource packets,
            IReadOnlyList<MatroskaCodecConfiguration> configurations, MatroskaIndexOptions options, CancellationToken token)
        {
            MatroskaPresentationIndex result = new(configurations.Count);
            bool success = false;
            try
            {
                using MatroskaVideoVisibility visibility = new(source, allocator);
                using MatroskaBlockReader blocks = new(source, allocator, options.MaximumBlockHeaderBytes, options.MaximumFrameBytes);
                for (int stream = 0; stream < configurations.Count; stream++)
                {
                    EncodedStreamInfo configuration = configurations[stream].Stream;
                    if (configuration.Kind != MediaKind.Video) { continue; }
                    long packetCount = index.GetPacketCount(stream);
                    if (packetCount == 0) { continue; }
                    int entryBytes = Unsafe.SizeOf<ContainerVideoPresentationUnit>();
                    if (packetCount > int.MaxValue / entryBytes)
                    { throw new NotSupportedException("The Matroska presentation index exceeds the maximum managed workspace size."); }
                    result.storage[stream] = index.AllocatePresentationStorage(checked((int)packetCount * entryBytes));
                    int cachedBlock = -1;
                    for (long packet = 0; packet < packetCount; packet++)
                    {
                        token.ThrowIfCancellationRequested();
                        MatroskaPacketAddress address = index.Locate(stream, packet);
                        MatroskaIndexedBlock block = index.GetBlock(address.BlockIndex);
                        bool shown = true;
                        if (configuration.CodecId is "vp8" or "vp9" || (block.Flags & 8) != 0)
                        {
                            if (cachedBlock != address.BlockIndex)
                            {
                                await blocks.ReadAsync(block.Element, token).ConfigureAwait(false);
                                cachedBlock = address.BlockIndex;
                            }
                            shown = await visibility.IsVisibleAsync(blocks.GetFrame(address.FrameIndex), configuration.CodecId,
                                (block.Flags & 8) != 0, token).ConfigureAwait(false);
                        }
                        if (!shown) { continue; }
                        ContainerPacketTiming timing = packets.GetPacketTiming(stream, packet, token);
                        result.Units(stream)[result.counts[stream]++] = new(packet, 0, timing.PresentationTime, timing.Duration);
                    }
                    result.ResolveDurations(stream, index, packets, token);
                }
                success = true;
                return result;
            }
            finally
            {
                if (!success) { result.Dispose(); }
            }
        }

        private void ResolveDurations(int stream, MatroskaPacketIndex index, MatroskaPacketSource packets, CancellationToken token)
        {
            Span<ContainerVideoPresentationUnit> units = Units(stream)[..counts[stream]];
            units.Sort(static (left, right) => Nullable.Compare(left.PresentationTime, right.PresentationTime));
            for (int position = 0; position < units.Length; position++)
            {
                token.ThrowIfCancellationRequested();
                ContainerVideoPresentationUnit unit = units[position];
                MatroskaPacketAddress address = index.Locate(stream, unit.PacketIndex);
                MediaDuration? duration = packets.GetExplicitPresentationDuration(address);
                if (position + 1 < units.Length &&
                    unit.PresentationTime is MediaTimestamp start && units[position + 1].PresentationTime is MediaTimestamp end)
                {
                    if (end.CompareTo(start) <= 0)
                    { throw new InvalidDataException("Matroska displayed pictures require distinct presentation timestamps."); }
                    duration ??= new MediaTimeRange(start, end).Duration;
                }
                units[position] = unit with { Duration = duration ?? unit.Duration };
            }
            units.Sort(static (left, right) => left.PacketIndex.CompareTo(right.PacketIndex));
        }

        public int GetCount(int stream)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(stream);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stream, counts.Length);
            return counts[stream];
        }

        public ContainerVideoPresentationUnit GetUnit(int stream, long unit)
        {
            int count = GetCount(stream);
            ArgumentOutOfRangeException.ThrowIfNegative(unit);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(unit, count);
            return Units(stream)[(int)unit];
        }

        public MediaDuration? GetDuration(int stream, long packet)
        {
            int low = 0, high = GetCount(stream);
            Span<ContainerVideoPresentationUnit> units = Units(stream);
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (units[middle].PacketIndex < packet) { low = middle + 1; }
                else { high = middle; }
            }
            return low < counts[stream] && units[low].PacketIndex == packet ? units[low].Duration : null;
        }

        private Span<ContainerVideoPresentationUnit> Units(int stream) => storage[stream] is WritableMediaBuffer buffer
            ? MemoryMarshal.Cast<byte, ContainerVideoPresentationUnit>(buffer.Memory.Span) : Span<ContainerVideoPresentationUnit>.Empty;

        public void Dispose()
        {
            foreach (WritableMediaBuffer? buffer in storage) { buffer?.Dispose(); }
        }
    }
}
