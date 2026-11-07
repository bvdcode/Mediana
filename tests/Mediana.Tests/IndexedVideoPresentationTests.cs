// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class IndexedVideoPresentationTests
    {
        [Fact]
        public void CodedPacketsMayDisplayZeroOrSeveralIndependentlyTimedPictures()
        {
            TimeBase clock = new(1, 1000);
            PresentationUnitTestReader reader = new([new(null, null, null, true), new(null, null, null, false), new(null, null, null, false)],
                [new(2, 0, new(30, clock), new(30, clock)), new(0, 1, new(10, clock), new(20, clock)), new(0, 0, new(0, clock), new(10, clock))]);
            MediaBufferAllocator allocator = new(65536);
            using (VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, 7, new(15, clock), allocator))
            {
                Assert.Equal(2, plan.Count);
                Assert.Equal(new MediaTimeRange(new(0, clock), new(30, clock)), plan[0]);
                Assert.Equal(new MediaTimeRange(new(30, clock), new(60, clock)), plan[1]);
                Assert.Equal(plan[0].Start.TimeBase, VideoSegmentPlanner.GetTimeBase(reader, 7));
                Assert.Equal(0, reader.MetadataReads);
                Assert.Equal(0, reader.PacketReads);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void IntermediateExplicitPresentationExtentsParticipateInTheExactClock()
        {
            TimeBase clock = new(1, 1000);
            PresentationUnitTestReader reader = new([new(null, null, null, true), new(null, null, null, false)],
                [new(0, 0, new(0, clock), new(1, new(1, 3000))), new(1, 0, new(10, clock), new(10, clock))]);
            MediaBufferAllocator allocator = new(65536);
            using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, 7, new(1, clock), allocator);
            Assert.Equal(new TimeBase(1, 3000), plan[0].Start.TimeBase);
            Assert.Equal(plan[0].Start.TimeBase, VideoSegmentPlanner.GetTimeBase(reader, 7));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(1, 0)]
        [InlineData(0, -1)]
        public void InvalidPresentationMappingsAreRejectedWithoutPayloadReads(long packet, int ordinal)
        {
            TimeBase clock = new(1, 1000);
            PresentationUnitTestReader reader = new([new(null, null, null, true)], [new(packet, ordinal, new(0, clock), new(10, clock))]);
            MediaBufferAllocator allocator = new(65536);
            Assert.Throws<InvalidDataException>(() => VideoSegmentPlanner.Create(reader, 7, new(1, clock), allocator));
            Assert.Equal(0, reader.MetadataReads);
            Assert.Equal(0, reader.PacketReads);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
