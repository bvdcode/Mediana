// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Execution
{
    /// <summary>Plans presentation-aligned encoding intervals from complete indexed video access units.
    /// Sorts clock metadata in allocator-owned workspace without reading compressed payload or decoding frames.
    /// Cuts follow target multiples from the first presentation timestamp, avoiding cumulative rounding drift.
    /// Every returned interval contains whole presentation units; compressed stream copy needs separate restart validation.</summary>
    public static class VideoSegmentPlanner
    {
        public static VideoSegmentPlan Create(IIndexedPacketTimingReader reader, int streamIndex,
            MediaDuration targetDuration, MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(allocator);
            targetDuration.TimeBase.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetDuration.Value);
            cancellationToken.ThrowIfCancellationRequested();
            EncodedStreamInfo stream = reader.Streams.SingleOrDefault(item => item.StreamIndex == streamIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(streamIndex));
            if (stream.Kind != MediaKind.Video || stream.Format.Boundary != EncodedPacketBoundary.AccessUnit)
            {
                throw new NotSupportedException("Video interval planning requires indexed complete video access units.");
            }
            long packetCount = reader.GetPacketCount(streamIndex);
            if (packetCount <= 0) { throw new InvalidDataException("The video stream contains no indexed presentation units."); }
            int unitBytes = Unsafe.SizeOf<VideoPresentationUnit>();
            if (packetCount > int.MaxValue / unitBytes)
            {
                throw new NotSupportedException("The video presentation index exceeds the maximum managed workspace size.");
            }
            using WritableMediaBuffer workspace = allocator.Allocate(checked((int)packetCount * unitBytes));
            Span<VideoPresentationUnit> units = MemoryMarshal.Cast<byte, VideoPresentationUnit>(workspace.Memory.Span);
            TimeBase clock = new(1, 1);
            for (int index = 0; index < units.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ContainerPacketTiming timing = reader.GetPacketTiming(streamIndex, index, cancellationToken);
                MediaTimestamp pts = timing.PresentationTime
                    ?? throw new NotSupportedException("Video interval planning requires every indexed presentation timestamp.");
                clock = TimeBase.Common(clock, pts.ReduceClock().TimeBase);
                units[index] = new(pts, timing.Duration, index);
            }
            units.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            cancellationToken.ThrowIfCancellationRequested();
            VideoPresentationUnit last = units[^1];
            MediaDuration length = (reader is IIndexedPresentationTimingReader presentation
                ? presentation.GetPresentationDuration(streamIndex, last.PacketIndex, cancellationToken) : last.Duration)
                ?? throw new NotSupportedException("The final video presentation interval is unknown.");
            if (length.Value == 0) { throw new InvalidDataException("The final video presentation interval must be positive."); }
            clock = TimeBase.Common(clock, new MediaTimestamp(length.Value, length.TimeBase).ReduceClock().TimeBase);
            for (int index = 0; index < units.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                units[index] = units[index] with { Start = units[index].Start.Rescale(clock, TimestampRounding.TowardZero) };
                if (index > 0 && units[index].Start.Value <= units[index - 1].Start.Value)
                {
                    throw new InvalidDataException("Video interval planning requires distinct presentation timestamps.");
                }
            }
            long first = units[0].Start.Value;
            long finalLength = new MediaTimestamp(length.Value, length.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value;
            long end = checked(units[^1].Start.Value + finalLength);
            long total = checked(end - first);
            BigInteger targetNumerator = (BigInteger)targetDuration.Value * targetDuration.TimeBase.Numerator * clock.Denominator;
            BigInteger targetDenominator = (BigInteger)targetDuration.TimeBase.Denominator * clock.Numerator;
            long threshold = NextThreshold(0, total, targetNumerator, targetDenominator);
            int count = 1;
            for (int index = 1; index < units.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long position = units[index].Start.Value;
                long elapsed = checked(position - first);
                if (elapsed < threshold) { continue; }
                count = checked(count + 1);
                threshold = NextThreshold(elapsed, total, targetNumerator, targetDenominator);
            }
            using WritableMediaBuffer output = allocator.Allocate(checked(count * Unsafe.SizeOf<MediaTimeRange>()));
            Span<MediaTimeRange> intervals = MemoryMarshal.Cast<byte, MediaTimeRange>(output.Memory.Span);
            long start = first;
            int segment = 0;
            threshold = NextThreshold(0, total, targetNumerator, targetDenominator);
            for (int index = 1; index < units.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long position = units[index].Start.Value;
                long elapsed = checked(position - first);
                if (elapsed < threshold) { continue; }
                intervals[segment++] = new(new(start, clock), new(position, clock));
                start = position;
                threshold = NextThreshold(elapsed, total, targetNumerator, targetDenominator);
            }
            intervals[segment] = new(new(start, clock), new(end, clock));
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = output.Publish();
            return new(buffer, count);
        }

        private static long NextThreshold(long elapsed, long total, BigInteger targetNumerator, BigInteger targetDenominator)
        {
            BigInteger multiple = elapsed * targetDenominator / targetNumerator + 1;
            BigInteger ticks = (multiple * targetNumerator + targetDenominator - 1) / targetDenominator;
            return ticks >= total ? total : (long)ticks;
        }
    }
}
