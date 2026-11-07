// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class VideoSegmentPlannerTests
    {
        [Theory]
        [InlineData(1001, 30000, 601, 6)]
        [InlineData(1001, 60000, 1201, 6)]
        [InlineData(1, 24, 601, 6)]
        [InlineData(10, 1, 12, 6)]
        public void FractionalClocksAndReorderingFollowAbsoluteTargetsWithoutAccumulatingError(int ticks, int frequency, int frames, int target)
        {
            TimeBase clock = new(1, frequency);
            ContainerPacketTiming[] timings = Enumerable.Range(0, frames).Reverse()
                .Select(index => new ContainerPacketTiming(new(index * ticks - 5L * ticks, clock),
                    new(index * ticks, clock), new(ticks, clock), index == 0)).ToArray();
            VideoTimingTestReader reader = new(timings, clock);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, 7, new(target, new(1, 1)), allocator);
            Assert.Equal(0, reader.PacketReads);
            Assert.Equal(frames, reader.MetadataReads);
            List<long> boundaries = [-5L * ticks];
            long multiple = 1;
            for (int index = 1; index < frames; index++)
            {
                if ((decimal)index * ticks < (decimal)multiple * target * frequency) { continue; }
                boundaries.Add((long)index * ticks - 5L * ticks);
                multiple = (long)decimal.Floor((decimal)index * ticks / target / frequency) + 1;
            }
            boundaries.Add((long)(frames - 5) * ticks);
            Assert.Equal(boundaries.Count - 1, plan.Count);
            for (int index = 0; index < plan.Count; index++)
            {
                Assert.Equal(new MediaTimestamp(boundaries[index], clock), plan[index].Start);
                Assert.Equal(new MediaTimestamp(boundaries[index + 1], clock), plan[index].End);
            }
            plan.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void VariablePresentationIntervalsUsePtsOrderAndPreserveThePartialTail()
        {
            TimeBase clock = new(1, 1000);
            VideoTimingTestReader reader = new(new long[] { 300, 0, 180, 100 }
                .Select(pts => new ContainerPacketTiming(new(pts, clock), null, new(70, clock), null)).ToArray(), clock);
            MediaBufferAllocator allocator = new(65536);
            using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, 7, new(150, clock), allocator);
            Assert.Equal(3, plan.Count);
            Assert.Equal(new MediaTimeRange(new(0, clock), new(180, clock)), plan[0]);
            Assert.Equal(new MediaTimeRange(new(180, clock), new(300, clock)), plan[1]);
            Assert.Equal(new MediaTimeRange(new(300, clock), new(370, clock)), plan[2]);
        }

        [Fact]
        public void TinyTargetsCreateOneWindowPerFrameWithoutRequiringAnExpandedClock()
        {
            TimeBase clock = new(1, 12288);
            VideoTimingTestReader reader = new(Enumerable.Range(0, 20)
                .Select(index => new ContainerPacketTiming(new(index * 512, clock), null, new(512, clock), true)).ToArray(), clock);
            MediaBufferAllocator allocator = new(65536);
            using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, 7, new(1, new(1, 1_000_000_000)), allocator);
            Assert.Equal(20, plan.Count);
            Assert.All(plan, interval => Assert.Equal(new MediaTimestamp(512, clock), new(interval.Duration.Value, interval.Duration.TimeBase)));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = plan[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = plan[plan.Count]);
            MediaTimeRange copied = plan[0];
            plan.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ObjectDisposedException>(() => _ = plan[0]);
            Assert.Equal(new MediaTimestamp(512, clock), new(copied.Duration.Value, copied.Duration.TimeBase));
        }

        [Fact]
        public void NanosecondDeclarationsAndFractionalFrameDurationsUseTheExactActualClock()
        {
            TimeBase clock = new(1, 1_000_000_000);
            VideoTimingTestReader reader = new(new long[] { 0, 42_000_000, 83_000_000 }
                .Select(pts => new ContainerPacketTiming(new(pts, clock), null, new(1, new(1, 24)), true)).ToArray(), clock);
            MediaBufferAllocator allocator = new(65536);
            using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, 7, new(1, new(1, 20)), allocator);
            Assert.Equal(2, plan.Count);
            Assert.Equal(new TimeBase(1, 3000), plan[0].Start.TimeBase);
            Assert.Equal(new MediaTimestamp(83, new(1, 1000)), plan[0].End);
            Assert.Equal(new MediaTimestamp(374, new(1, 3000)), plan[1].End);
        }

        [Fact]
        public void UnknownOrDuplicatePresentationFactsAreRejectedWithoutLeakingTheIndex()
        {
            TimeBase clock = new(1, 1000);
            MediaBufferAllocator allocator = new(65536);
            VideoTimingTestReader duplicate = new([new(new(0, clock), null, new(10, clock), true),
                new(new(0, clock), null, new(10, clock), false)], clock);
            Assert.Throws<InvalidDataException>(() => VideoSegmentPlanner.Create(duplicate, 7, new(1, clock), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            VideoTimingTestReader unknownEnd = new([new(new(0, clock), null, null, true)], clock);
            Assert.Throws<NotSupportedException>(() => VideoSegmentPlanner.Create(unknownEnd, 7, new(1, clock), allocator));
            VideoTimingTestReader unknownPts = new([new(null, null, new(10, clock), true)], clock);
            Assert.Throws<NotSupportedException>(() => VideoSegmentPlanner.Create(unknownPts, 7, new(1, clock), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CancellationAndOutputBudgetFailureReleasePlanningStorage(bool cancel)
        {
            TimeBase clock = new(1, 25);
            MediaBufferAllocator allocator = new(65536);
            VideoTimingTestReader reader = new(Enumerable.Range(0, 20)
                .Select(index => new ContainerPacketTiming(new(index, clock), null, new(1, clock), true)).ToArray(), clock);
            using CancellationTokenSource cancellation = new();
            using WritableMediaBuffer occupied = allocator.Allocate(1);
            WritableMediaBuffer? temporary = null;
            reader.AfterRead = () =>
            {
                if (reader.MetadataReads != 20) { return; }
                if (cancel) { cancellation.Cancel(); }
                else { temporary = allocator.Allocate((int)(allocator.LimitBytes - allocator.AllocatedBytes)); }
            };
            if (cancel)
            {
                Assert.Throws<OperationCanceledException>(() => VideoSegmentPlanner.Create(reader, 7, new(1, clock), allocator, cancellation.Token));
            }
            else
            {
                Assert.Throws<MediaMemoryLimitException>(() => VideoSegmentPlanner.Create(reader, 7, new(1, clock), allocator));
            }
            temporary?.Dispose(); occupied.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void TimeRangesAndCommonClocksPreserveExactRationalBoundaries()
        {
            Assert.Equal(new TimeBase(1, 240000), TimeBase.Common(new(1001, 30000), new(1, 48000)));
            Assert.Equal(new MediaTimestamp(1, new(1, 25)), new MediaTimestamp(40_000_000, new(1, 1_000_000_000)).ReduceClock());
            Assert.Equal(new MediaTimestamp(long.MinValue, new(1, 1)), new MediaTimestamp(long.MinValue, new(1, 1)).ReduceClock());
            Assert.Equal(new MediaTimestamp(0, new(1, 1)), new MediaTimestamp(0, new(1, 1_000_000_000)).ReduceClock());
            MediaTimeRange interval = new(new(-1, new(1, 3)), new(1, new(1, 7)));
            Assert.Equal(new MediaTimestamp(-7, new(1, 21)), interval.Start);
            Assert.Equal(new MediaTimestamp(3, new(1, 21)), interval.End);
            Assert.Equal(new MediaDuration(10, new(1, 21)), interval.Duration);
            Assert.Equal(interval, new MediaTimeRange(new(-7, new(1, 21)), new(3, new(1, 21))));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTimeRange(interval.End, interval.Start));
            Assert.Throws<ArgumentException>(() => TimeBase.Common());
            Assert.Throws<ArgumentException>(() => TimeBase.Common(default(TimeBase)));
            Assert.Throws<NotSupportedException>(() => TimeBase.Common(new(1, int.MaxValue), new(1, int.MaxValue - 1)));
        }
    }
}
