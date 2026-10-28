using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264FrameQueueTests
    {
        [Fact]
        public void ReordersPicturesWithTheirOriginalTimestampAndDrainsTheTail()
        {
            MediaBufferAllocator allocator = new(1024);
            using (H264FrameQueue queue = new())
            {
                Add(queue, allocator, 0, idr: true);
                Add(queue, allocator, 6);
                Assert.False(queue.HasReady);
                Assert.Equal(2, queue.PendingCount);
                Add(queue, allocator, 2);
                Receive(queue, 0);
                Add(queue, allocator, 4);
                Receive(queue, 2);
                Add(queue, allocator, 8);
                Receive(queue, 4);
                queue.Drain();
                Receive(queue, 6);
                Receive(queue, 8);
                Assert.Null(queue.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void IdrFlushesOrDiscardsPriorPendingFrames(bool discard)
        {
            MediaBufferAllocator allocator = new(1024);
            using (H264FrameQueue queue = new())
            {
                Add(queue, allocator, 0, idr: true);
                Add(queue, allocator, 4);
                VideoFrame current = FrameFixture.Create(allocator, 1, 1);
                queue.Add(current, new(0, 0), Header() with { IsIdr = true, NoOutputOfPriorPictures = discard });
                if (!discard)
                {
                    Receive(queue, 0);
                    Receive(queue, 4);
                }
                Assert.False(queue.HasReady);
                Assert.Equal(1, queue.PendingCount);
                queue.Drain();
                Receive(queue, 1);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void MemoryResetDrainsPriorOrderBeforeStartingAnotherOrderEpoch()
        {
            MediaBufferAllocator allocator = new(1024);
            using (H264FrameQueue queue = new())
            {
                Add(queue, allocator, 0, idr: true);
                Add(queue, allocator, 4);
                queue.Add(FrameFixture.Create(allocator, 9, 9), new(0, 0), Header() with
                {
                    MemoryOperations = [new H264MemoryOperation(5)]
                });
                Receive(queue, 0);
                Receive(queue, 4);
                Add(queue, allocator, 2);
                queue.Drain();
                Receive(queue, 9);
                Receive(queue, 2);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BackpressureAndInvalidOrderDoNotTakeInputOwnership()
        {
            MediaBufferAllocator allocator = new(1024);
            using (H264FrameQueue queue = new())
            {
                Add(queue, allocator, 0, idr: true);
                using VideoFrame duplicate = FrameFixture.Create(allocator, 9);
                Assert.Throws<InvalidDataException>(() => queue.Add(duplicate, new(0, 0), Header()));
                Add(queue, allocator, 4);
                Add(queue, allocator, 2);
                Assert.Throws<InvalidOperationException>(() => queue.Add(duplicate, new(6, 6), Header()));
                Receive(queue, 0);
                Assert.Throws<InvalidDataException>(() => queue.Add(duplicate, new(-2, -2), Header()));
                Assert.Equal(9, duplicate.Data.Span[0]);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ReturnedFramesSurviveQueueDisposalAndTypeTwoDoesNotDelayOutput()
        {
            MediaBufferAllocator allocator = new(1024);
            H264FrameQueue queue = new();
            VideoFrame input = FrameFixture.Create(allocator, 7);
            queue.Add(input, new(0, 0), Header() with { IsIdr = true, Sequence = Header().Sequence with { PictureOrderType = 2 } });
            using VideoFrame output = Assert.IsType<VideoFrame>(queue.Receive());
            queue.Dispose();
            Assert.Equal(7, output.Data.Span[0]);
            Assert.Equal(4, allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<ObjectDisposedException>(() => queue.Receive());
        }

        [Fact]
        public void LevelLimitsConstrainReferenceAndOutputBuffering()
        {
            H264SequenceParameterSet sequence = Header().Sequence with
            {
                Level = 31,
                WidthInMbs = 80,
                HeightInMapUnits = 45,
                ReferenceFrames = 4,
                Vui = null
            };
            Assert.Equal(new H264PictureBufferLimits(5, 5), H264PictureBufferLimits.Read(sequence));
            Assert.Throws<InvalidDataException>(() => H264PictureBufferLimits.Read(sequence with { ReferenceFrames = 6 }));
            Assert.Throws<InvalidDataException>(() => H264PictureBufferLimits.Read(sequence with
            {
                Vui = new H264VuiParameters { MaxReorderFrames = 6, MaxDecodedFrameBuffering = 6 }
            }));
            Assert.Throws<NotSupportedException>(() => H264PictureBufferLimits.Read(sequence with { Level = 99 }));
            sequence = Header().Sequence with { Profile = 66, Level = 11, WidthInMbs = 15, HeightInMapUnits = 15, Vui = null };
            Assert.Equal(new H264PictureBufferLimits(4, 4), H264PictureBufferLimits.Read(sequence));
            Assert.Equal(new H264PictureBufferLimits(1, 1), H264PictureBufferLimits.Read(sequence with { Constraints = 16 }));
        }

        private static void Add(H264FrameQueue queue, MediaBufferAllocator allocator, int order, bool idr = false) =>
            queue.Add(FrameFixture.Create(allocator, (byte)order, order), new(order, order), Header() with { IsIdr = idr });

        private static void Receive(H264FrameQueue queue, int expected)
        {
            using VideoFrame output = Assert.IsType<VideoFrame>(queue.Receive());
            Assert.Equal(expected, output.Data.Span[0]);
            Assert.Equal(expected, output.Timestamp!.Value.Value);
        }

        private static H264SliceHeader Header() => new(new H264SequenceParameterSet
        {
            Profile = 77,
            Level = 10,
            WidthInMbs = 1,
            HeightInMapUnits = 1,
            FrameMbsOnly = true,
            ReferenceFrames = 1,
            Vui = new H264VuiParameters { MaxReorderFrames = 2, MaxDecodedFrameBuffering = 3 }
        }, new H264PictureParameterSet(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0));
    }
}
