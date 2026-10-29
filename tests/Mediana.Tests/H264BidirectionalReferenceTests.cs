using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264BidirectionalReferenceTests
    {
        [Fact]
        public Task RealBSequenceListsUseParsedReferencePictureOrder() =>
            H264BidirectionalTestInput.VerifyReferenceListsAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Fact]
        public void BothListsUsePictureOrderAndPadUnusedEntries()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer buffer = new())
            {
                Store(buffer, allocator, Header(0, idr: true), new(0, 0));
                Store(buffer, allocator, Header(1), new(8, 8));
                Store(buffer, allocator, Header(2), new(10, 2));
                Store(buffer, allocator, Header(3), new(6, 12));
                H264BidirectionalReferences lists = buffer.GetBidirectionalLists(Header(4), new(4, 4));
                Assert.Equal(new int?[] { 2, 0, 3, 1, null, null }, Numbers(lists.L0));
                Assert.Equal(new int?[] { 3, 1, 2, 0, null, null }, Numbers(lists.L1));
                Assert.Equal(new H264PictureOrder(10, 2), lists.L0[0]!.Order);
                Assert.Equal(new int?[] { 3, 2, 1, 0, null, null }, Numbers(buffer.GetList0(Header(4) with { Kind = H264SliceKind.P })));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(-2, 0, 1)]
        [InlineData(4, 1, 0)]
        public void IdenticalInitialListsSwapBeforeApplyingActiveCounts(int current, int l0, int l1)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true), new(0, 0));
            Store(buffer, allocator, Header(1), new(2, 2));
            H264SliceHeader header = Header(2) with { ActiveReferencesL0 = 1, ActiveReferencesL1 = 2 };
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(header, new(current, current));
            Assert.Equal(new int?[] { l0 }, Numbers(lists.L0));
            Assert.Equal(new int?[] { l1, l0 }, Numbers(lists.L1));
        }

        [Fact]
        public void ASingleReferenceIsSharedByBothLists()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true), new(0, 0));
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(Header(1), new(2, 2));
            Assert.Same(lists.L0[0], lists.L1[0]);
            Assert.Equal(new int?[] { 0, null, null, null, null, null }, Numbers(lists.L0));
            Assert.Equal(Numbers(lists.L0), Numbers(lists.L1));
        }

        [Fact]
        public void LongTermEntriesFollowShortTermPicturesInBothLists()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true) with { LongTermReference = true }, new(0, 0));
            Store(buffer, allocator, Header(1) with
            {
                AdaptiveMarking = true,
                MemoryOperations = [new(4, MaxLongTermFrameIndexPlusOne: 2), new(6, LongTermFrameIndex: 1)]
            }, new(8, 8));
            Store(buffer, allocator, Header(2), new(2, 2));
            Store(buffer, allocator, Header(3), new(6, 6));
            H264SliceHeader header = Header(4) with { ActiveReferencesL0 = 4, ActiveReferencesL1 = 4 };
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(header, new(4, 4));
            Assert.Equal(new int?[] { 2, 3, 0, 1 }, Numbers(lists.L0));
            Assert.Equal(new int?[] { 3, 2, 0, 1 }, Numbers(lists.L1));
            lists = buffer.GetBidirectionalLists(header with
            {
                ReferenceModifications = [new(2, 1)],
                ReferenceModificationsL1 = [new(2, 0)]
            }, new(4, 4));
            Assert.Equal(new int?[] { 1, 2, 3, 0 }, Numbers(lists.L0));
            Assert.Equal(new int?[] { 0, 3, 2, 1 }, Numbers(lists.L1));
        }

        [Fact]
        public void EachListHasItsOwnPictureNumberPredictorAndPreservesEarlierDuplicates()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            for (int frame = 0; frame < 4; frame++)
            {
                Store(buffer, allocator, Header(frame, idr: frame == 0), new(frame * 2, frame * 2));
            }
            H264SliceHeader header = Header(4) with
            {
                ActiveReferencesL0 = 3,
                ActiveReferencesL1 = 3,
                ReferenceModifications = [new(0, 2), new(1, 15)],
                ReferenceModificationsL1 = [new(0, 1)]
            };
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(header, new(8, 8));
            Assert.Equal(new int?[] { 1, 1, 3 }, Numbers(lists.L0));
            Assert.Equal(new int?[] { 2, 3, 1 }, Numbers(lists.L1));
        }

        [Fact]
        public void LongTermOnlyListsAreSwappedBeforeTruncation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true) with { LongTermReference = true }, new(0, 0));
            Store(buffer, allocator, Header(1) with
            {
                AdaptiveMarking = true,
                MemoryOperations = [new(4, MaxLongTermFrameIndexPlusOne: 2), new(6, LongTermFrameIndex: 1)]
            }, new(2, 2));
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(
                Header(2) with { ActiveReferencesL0 = 1, ActiveReferencesL1 = 1 }, new(4, 4));
            Assert.Equal(new int?[] { 0 }, Numbers(lists.L0));
            Assert.Equal(new int?[] { 1 }, Numbers(lists.L1));
        }

        [Fact]
        public void MissingListOneReferenceLeavesStorageAndPreviousListsIntact()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true), new(0, 0));
            long bytes = allocator.AllocatedBytes;
            H264SliceHeader header = Header(1) with { ReferenceModificationsL1 = [new(2, 1)] };
            Assert.Throws<InvalidDataException>(() => buffer.GetBidirectionalLists(header, new(2, 2)));
            Assert.Equal(bytes, allocator.AllocatedBytes);
            Assert.Equal(1, buffer.Count);
            Assert.Equal(0, buffer.GetBidirectionalLists(Header(1), new(2, 2)).L1[0]!.FrameNumber);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 0)]
        [InlineData(33, 1)]
        [InlineData(1, 33)]
        public void BothActiveCountsMustBeInRange(int l0, int l1)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true), new(0, 0));
            H264SliceHeader header = Header(1) with { ActiveReferencesL0 = l0, ActiveReferencesL1 = l1 };
            Assert.Throws<InvalidDataException>(() => buffer.GetBidirectionalLists(header, new(2, 2)));
        }

        private static int?[] Numbers(IReadOnlyList<H264ReferencePicture?> references) => references.Select(value => value?.FrameNumber).ToArray();

        private static void Store(H264ReferenceBuffer buffer, MediaBufferAllocator allocator, H264SliceHeader header, H264PictureOrder order) =>
            buffer.Store(new H264Picture(header.Sequence, allocator, header.Kind), header, order);

        private static H264SliceHeader Header(int number, bool idr = false)
        {
            H264SequenceParameterSet sequence = new()
            {
                FrameNumberBits = 4,
                ReferenceFrames = 6,
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                Width = 16,
                Height = 16,
                FrameMbsOnly = true
            };
            H264PictureParameterSet picture = new(0, 0, false, false, 26, 0, 0, true, false, false, false, 6, 6, false, 0);
            return new H264SliceHeader(sequence, picture)
            {
                Kind = idr switch { true => H264SliceKind.I, false => H264SliceKind.B },
                IsIdr = idr,
                ReferenceIndicator = 3,
                FrameNumber = number,
                ActiveReferencesL0 = 6,
                ActiveReferencesL1 = 6
            };
        }
    }
}
