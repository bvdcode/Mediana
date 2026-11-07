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
    /// Every returned interval contains whole presentation units; compressed stream copy needs separate restart validation.
    /// Explicit container presentation ranges clip source display extents before planning.
    /// Readers without IIndexedVideoPresentationReader must expose exactly one displayed unit per indexed packet.</summary>
    public static class VideoSegmentPlanner
    {
        /// <summary>Derives an exact clock from actual indexed timestamps and known presentation durations.
        /// Uses constant workspace without sorting, allocating a frame index or reading media payload.</summary>
        public static TimeBase GetTimeBase(IIndexedPacketTimingReader reader, int streamIndex,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            cancellationToken.ThrowIfCancellationRequested();
            EncodedStreamInfo stream = reader.Streams.SingleOrDefault(item => item.StreamIndex == streamIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(streamIndex));
            if (stream.Kind != MediaKind.Video || stream.Format.Boundary != EncodedPacketBoundary.AccessUnit)
            { throw new NotSupportedException("Video clock planning requires indexed complete video access units."); }
            long count = UnitCount(reader, streamIndex);
            if (count <= 0) { throw new InvalidDataException("The video stream contains no indexed presentation units."); }
            MediaTimeRange? selection = (reader as IContainerPresentationReader)?.GetPresentationRange(streamIndex, cancellationToken);
            TimeBase clock = new(1, 1);
            VideoPresentationUnit? last = null;
            VideoPresentationUnit? preceding = null;
            MediaTimestamp? following = null;
            for (long index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VideoPresentationUnit unit = Unit(reader, streamIndex, index, cancellationToken);
                if (selection is MediaTimeRange range)
                {
                    if (unit.Start.CompareTo(range.Start) < 0)
                    {
                        if (preceding is null || unit.Start.CompareTo(preceding.Value.Start) > 0) { preceding = unit; }
                        continue;
                    }
                    if (following is null || unit.Start.CompareTo(following.Value) < 0) { following = unit.Start; }
                }
                if (Project(unit, selection) is not VideoPresentationUnit displayed) { continue; }
                clock = IncludeClock(clock, displayed);
                if (last is null || displayed.Start.CompareTo(last.Value.Start) > 0) { last = displayed; }
            }
            if (preceding is VideoPresentationUnit previous && Project(previous, selection, following) is VideoPresentationUnit opening)
            {
                clock = IncludeClock(clock, opening);
                if (last is null) { last = opening; }
            }
            if (last is null) { throw new InvalidDataException("The video presentation range contains no indexed presentation units."); }
            MediaDuration duration = FinalDuration(last.Value);
            return TimeBase.Common(clock, new MediaTimestamp(duration.Value, duration.TimeBase).ReduceClock().TimeBase);
        }

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
            long packetCount = UnitCount(reader, streamIndex);
            if (packetCount <= 0) { throw new InvalidDataException("The video stream contains no indexed presentation units."); }
            int unitBytes = Unsafe.SizeOf<VideoPresentationUnit>();
            if (packetCount > int.MaxValue / unitBytes)
            {
                throw new NotSupportedException("The video presentation index exceeds the maximum managed workspace size.");
            }
            using WritableMediaBuffer workspace = allocator.Allocate(checked((int)packetCount * unitBytes));
            Span<VideoPresentationUnit> units = MemoryMarshal.Cast<byte, VideoPresentationUnit>(workspace.Memory.Span);
            for (int index = 0; index < units.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                units[index] = Unit(reader, streamIndex, index, cancellationToken);
            }
            units.Sort(static (left, right) => left.Start.CompareTo(right.Start));
            cancellationToken.ThrowIfCancellationRequested();
            MediaTimeRange? selection = (reader as IContainerPresentationReader)?.GetPresentationRange(streamIndex, cancellationToken);
            TimeBase clock = new(1, 1);
            int selected = 0;
            for (int index = 0; index < units.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VideoPresentationUnit unit = units[index];
                MediaTimestamp? following = index + 1 < units.Length ? units[index + 1].Start : null;
                if (selection is MediaTimeRange range && unit.Start.CompareTo(range.Start) < 0 &&
                    following is MediaTimestamp next && next.CompareTo(range.Start) <= 0) { continue; }
                if (Project(unit, selection, following) is not VideoPresentationUnit displayed) { continue; }
                units[selected++] = displayed;
                clock = IncludeClock(clock, displayed);
            }
            if (selected == 0) { throw new InvalidDataException("The video presentation range contains no indexed presentation units."); }
            units = units[..selected];
            VideoPresentationUnit last = units[^1];
            MediaDuration length = FinalDuration(last);
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

        private static TimeBase IncludeClock(TimeBase clock, VideoPresentationUnit unit)
        {
            clock = TimeBase.Common(clock, unit.Start.ReduceClock().TimeBase);
            return unit.Duration is MediaDuration extent
                ? TimeBase.Common(clock, new MediaTimestamp(extent.Value, extent.TimeBase).ReduceClock().TimeBase) : clock;
        }

        private static VideoPresentationUnit? Project(VideoPresentationUnit unit, MediaTimeRange? selection,
            MediaTimestamp? following = null)
        {
            if (selection is not MediaTimeRange range) { return unit; }
            if (unit.Start.CompareTo(range.End) >= 0 || range.Duration.Value == 0) { return null; }
            if (unit.Duration is not MediaDuration duration)
            {
                if (unit.Start.CompareTo(range.Start) < 0)
                { throw new NotSupportedException("The leading video presentation interval is unknown."); }
                return unit;
            }
            TimeBase clock = TimeBase.Common(unit.Start.ReduceClock().TimeBase,
                new MediaTimestamp(duration.Value, duration.TimeBase).ReduceClock().TimeBase);
            MediaTimestamp end = new(checked(unit.Start.Rescale(clock, TimestampRounding.TowardZero).Value +
                new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value), clock);
            if (following is MediaTimestamp next && unit.Start.CompareTo(range.Start) < 0 && next.CompareTo(end) < 0) { end = next; }
            if (end.CompareTo(range.Start) <= 0) { return null; }
            MediaTimestamp start = unit.Start.CompareTo(range.Start) < 0 ? range.Start : unit.Start;
            if (range.End.CompareTo(end) < 0) { end = range.End; }
            MediaTimeRange projected = new(start.ReduceClock(), end.ReduceClock());
            return unit with { Start = projected.Start, Duration = projected.Duration };
        }

        private static MediaDuration FinalDuration(VideoPresentationUnit last)
        {
            MediaDuration duration = last.Duration ?? throw new NotSupportedException("The final video presentation interval is unknown.");
            if (duration.Value == 0) { throw new InvalidDataException("The final video presentation interval must be positive."); }
            return duration;
        }

        private static long UnitCount(IIndexedPacketTimingReader reader, int streamIndex) =>
            reader is IIndexedVideoPresentationReader presentation ? presentation.GetPresentationUnitCount(streamIndex)
                : reader.GetPacketCount(streamIndex);

        private static VideoPresentationUnit Unit(IIndexedPacketTimingReader reader, int streamIndex, long index,
            CancellationToken token)
        {
            if (reader is IIndexedVideoPresentationReader presentation)
            {
                ContainerVideoPresentationUnit unit = presentation.GetPresentationUnit(streamIndex, index, token);
                if (unit.PacketIndex < 0 || unit.PacketIndex >= reader.GetPacketCount(streamIndex) || unit.OutputOrdinal < 0)
                { throw new InvalidDataException("The video presentation index refers to an invalid coded packet or output ordinal."); }
                return new(unit.PresentationTime ?? throw new NotSupportedException("Video planning requires every indexed presentation timestamp."),
                    unit.Duration, unit.PacketIndex);
            }
            ContainerPacketTiming timing = reader.GetPacketTiming(streamIndex, index, token);
            return new(timing.PresentationTime ?? throw new NotSupportedException("Video planning requires every indexed presentation timestamp."),
                reader is IIndexedPresentationTimingReader durations ? durations.GetPresentationDuration(streamIndex, index, token) : timing.Duration, index);
        }
    }
}
