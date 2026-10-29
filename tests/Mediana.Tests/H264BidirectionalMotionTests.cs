using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BidirectionalMotionTests
    {
        [Fact]
        public void ListsKeepIndependentReferencesVectorsAndPrediction() => H264BidirectionalTestInput.VerifyMotion();

        [Theory]
        [InlineData((int)H264SliceKind.I)]
        [InlineData((int)H264SliceKind.P)]
        public void SingleListPicturesKeepTheirOriginalMemoryBudget(int kind)
        {
            MediaBufferAllocator allocator = new(570);
            using (H264Picture picture = new(Sequence(), allocator, (H264SliceKind)kind))
            {
                Assert.Equal(570, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => picture.MotionAt(H264ReferenceList.L1, 0, 0));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BMotionFitsTheExactBudgetAndDoesNotOverlapOtherPictureState()
        {
            MediaBufferAllocator allocator = new(714);
            using (H264Picture picture = new(Sequence(), allocator, H264SliceKind.B))
            {
                picture.Quantizers.Fill(27);
                picture.Transforms8x8.Fill(1);
                picture.Counts(0).Fill(3);
                picture.Plane(2).Fill(155);
                picture.SetMotion(H264ReferenceList.L0, 0, 0, 16, 16, 31, -32768, 32767);
                picture.SetMotion(H264ReferenceList.L1, 0, 0, 16, 16, 17, 300, -400);
                Assert.All(picture.Quantizers.ToArray(), value => Assert.Equal(27, value));
                Assert.All(picture.Transforms8x8.ToArray(), value => Assert.Equal(1, value));
                Assert.All(picture.Counts(0).ToArray(), value => Assert.Equal(3, value));
                Assert.All(picture.Plane(2).ToArray(), value => Assert.Equal(155, value));
                Assert.Equal(714, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            MediaBufferAllocator insufficient = new(713);
            Assert.Throws<MediaMemoryLimitException>(() => new H264Picture(Sequence(), insufficient, H264SliceKind.B));
            Assert.Equal(0, insufficient.AllocatedBytes);
        }

        [Fact]
        public void ClearingOneListKeepsTheOtherAndMarksTheBlockAvailable()
        {
            MediaBufferAllocator allocator = new(1024);
            using H264Picture picture = new(Sequence(), allocator, H264SliceKind.B);
            Assert.Equal(new H264MotionSample(false, -1, 0, 0), picture.MotionAt(H264ReferenceList.L1, 0, 0));
            picture.SetMotion(H264ReferenceList.L0, 0, 0, 4, 4, 2, 8, -4);
            Assert.Equal(new H264MotionSample(true, -1, 0, 0), picture.MotionAt(H264ReferenceList.L1, 0, 0));
            picture.SetMotion(H264ReferenceList.L1, 0, 0, 4, 4, 3, 16, 20);
            picture.SetMotion(H264ReferenceList.L1, 0, 0, 4, 4, -1, 100, 100);
            Assert.Equal(new H264MotionSample(true, -1, 0, 0), picture.MotionAt(H264ReferenceList.L1, 0, 0));
            Assert.Equal(new H264MotionSample(true, 2, 8, -4), picture.MotionAt(H264ReferenceList.L0, 0, 0));
            picture.SetReference(H264ReferenceList.L0, 0, 0, 4, 4, -1);
            Assert.Equal(new H264MotionSample(true, -1, 0, 0), picture.MotionAt(H264ReferenceList.L0, 0, 0));
        }

        [Theory]
        [InlineData(-1, 0, 4, 4, 0)]
        [InlineData(0, 0, 20, 4, 0)]
        [InlineData(0, 0, 4, 20, 0)]
        [InlineData(0, 0, 3, 4, 0)]
        [InlineData(1, 0, 4, 4, 0)]
        [InlineData(0, 0, 4, 4, -2)]
        [InlineData(0, 0, 4, 4, 32)]
        public void InvalidPartitionsDoNotPartiallyModifyMotion(int x, int y, int width, int height, int reference)
        {
            using H264Picture picture = new(Sequence(), new MediaBufferAllocator(1024), H264SliceKind.B);
            picture.SetMotion(H264ReferenceList.L1, 0, 0, 16, 16, 1, 12, -8);
            Assert.Throws<ArgumentOutOfRangeException>(() => picture.SetMotion(H264ReferenceList.L1, x, y, width, height, reference, 0, 0));
            Assert.All(picture.ReferenceIndices(H264ReferenceList.L1).ToArray(), value => Assert.Equal(1, value));
            Assert.All(picture.MotionX(H264ReferenceList.L1).ToArray(), value => Assert.Equal(12, value));
            Assert.All(picture.MotionY(H264ReferenceList.L1).ToArray(), value => Assert.Equal(-8, value));
        }

        private static H264SequenceParameterSet Sequence() => new()
        {
            WidthInMbs = 1,
            HeightInMapUnits = 1,
            FrameMbsOnly = true,
            Width = 16,
            Height = 16
        };
    }
}
