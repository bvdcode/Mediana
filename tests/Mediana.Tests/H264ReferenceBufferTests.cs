// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264ReferenceBufferTests
    {
        [Fact]
        public void SlidingWindowOrdersReferencesAndReleasesOldPictures()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer buffer = new())
            {
                Store(buffer, allocator, Header(0, idr: true));
                long pictureBytes = allocator.AllocatedBytes;
                Assert.Equal(new int?[] { 0, null, null }, Numbers(buffer.GetList0(Header(1))));
                for (int number = 1; number <= 3; number++)
                {
                    Store(buffer, allocator, Header(number));
                }
                Assert.Equal(new int?[] { 3, 2, 1 }, Numbers(buffer.GetList0(Header(4))));
                Assert.Equal(pictureBytes * 3, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FrameNumbersWrapAndExplicitListChangesPreserveEarlierDuplicates()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true));
            for (int index = 1; index <= 17; index++)
            {
                Store(buffer, allocator, Header(index % 16));
            }
            H264SliceHeader header = Header(2);
            Assert.Equal(new int?[] { 1, 0, 15 }, Numbers(buffer.GetList0(header)));
            header = header with { ReferenceModifications = [new(0, 2), new(1, 0)] };
            Assert.Equal(new int?[] { 15, 0, 1 }, Numbers(buffer.GetList0(header)));
            header = Header(2) with { ReferenceModifications = [new(0, 0), new(1, 15)] };
            Assert.Equal(new int?[] { 1, 1, 0 }, Numbers(buffer.GetList0(header)));
        }

        [Fact]
        public void AdaptiveMarkingConvertsAndRemovesShortAndLongTermReferences()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true) with { LongTermReference = true });
            Store(buffer, allocator, Header(1));
            Store(buffer, allocator, Header(2) with
            {
                AdaptiveMarking = true,
                MemoryOperations =
                [new(4, MaxLongTermFrameIndexPlusOne: 2), new(3, DifferenceMinusOne: 0, LongTermFrameIndex: 1)]
            });
            IReadOnlyList<H264ReferencePicture?> list = buffer.GetList0(Header(3));
            Assert.Equal(new int?[] { 2, 0, 1 }, Numbers(list));
            Assert.Equal(new int?[] { null, 0, 1 }, list.Select(value => value?.LongTermIndex));
            H264SliceHeader reordered = Header(3) with { ReferenceModifications = [new(2, 1)] };
            Assert.Equal(new int?[] { 1, 2, 0 }, Numbers(buffer.GetList0(reordered)));
            Store(buffer, allocator, Header(3) with
            {
                AdaptiveMarking = true,
                MemoryOperations =
                [new(1, DifferenceMinusOne: 0), new(2, LongTermPictureNumber: 0), new(6, LongTermFrameIndex: 0)]
            });
            Assert.Equal(new int?[] { 3, 1, null }, Numbers(buffer.GetList0(Header(4))));
            Store(buffer, allocator, Header(4) with
            {
                AdaptiveMarking = true,
                MemoryOperations =
                [new(4, MaxLongTermFrameIndexPlusOne: 0)]
            });
            Assert.Equal(new int?[] { 4, null, null }, Numbers(buffer.GetList0(Header(5))));
        }

        [Fact]
        public void ReferenceResetRenumbersCurrentPictureAndIdrReleasesPriorSequence()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true));
            Store(buffer, allocator, Header(1));
            Store(buffer, allocator, Header(2) with { AdaptiveMarking = true, MemoryOperations = [new(5)] });
            Assert.Equal(new int?[] { 0, null, null }, Numbers(buffer.GetList0(Header(1))));
            long onePicture = allocator.AllocatedBytes;
            Store(buffer, allocator, Header(0, idr: true) with { LongTermReference = true });
            Assert.Equal(1, buffer.Count);
            Assert.Equal(onePicture, allocator.AllocatedBytes);
            Assert.Equal(0, buffer.GetList0(Header(1))[0]!.LongTermIndex);
        }

        [Fact]
        public void InvalidMarkingLeavesPriorPicturesAndInputOwnershipIntact()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true));
            H264SliceHeader invalid = Header(1) with
            {
                AdaptiveMarking = true,
                MemoryOperations =
                [new(4, MaxLongTermFrameIndexPlusOne: 2), new(1, DifferenceMinusOne: 0), new(2, LongTermPictureNumber: 1)]
            };
            using H264Picture input = new(invalid.Sequence, allocator, invalid.Kind);
            long before = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => buffer.Store(input, invalid, new H264PictureOrder(2, 2)));
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(384, input.Plane(0).Length + input.Plane(1).Length + input.Plane(2).Length);
            Assert.Equal(new int?[] { 0, null, null }, Numbers(buffer.GetList0(Header(1))));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void RejectsDuplicateSingletonMemoryOperations(int operation)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true) with { LongTermReference = true });
            H264SliceHeader invalid = Header(1) with { AdaptiveMarking = true, MemoryOperations = [new(operation), new(operation)] };
            using H264Picture input = new(invalid.Sequence, allocator, invalid.Kind);
            Assert.Throws<InvalidDataException>(() => buffer.Store(input, invalid, new H264PictureOrder(2, 2)));
            Assert.Equal(1, buffer.Count);
        }

        [Fact]
        public void NonReferencePictureIsReleasedAndMissingReferencesAreExplicit()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, idr: true));
            long before = allocator.AllocatedBytes;
            Store(buffer, allocator, Header(1) with { ReferenceIndicator = 0 });
            Assert.Equal(before, allocator.AllocatedBytes);
            H264SliceHeader missing = Header(1) with { ReferenceModifications = [new(2, 1)] };
            Assert.Throws<InvalidDataException>(() => buffer.GetList0(missing));
        }

        private static int?[] Numbers(IReadOnlyList<H264ReferencePicture?> references) => references.Select(value => value?.FrameNumber).ToArray();

        private static void Store(H264ReferenceBuffer buffer, MediaBufferAllocator allocator, H264SliceHeader header) =>
            buffer.Store(new H264Picture(header.Sequence, allocator, header.Kind), header, new H264PictureOrder(header.FrameNumber * 2, header.FrameNumber * 2));

        private static H264SliceHeader Header(int number, bool idr = false)
        {
            H264SequenceParameterSet sequence = new()
            {
                FrameNumberBits = 4,
                ReferenceFrames = 3,
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                Width = 16,
                Height = 16,
                FrameMbsOnly = true
            };
            H264PictureParameterSet picture = new(0, 0, false, false, 26, 0, 0, true, false, false, false, 3, 1, false, 0);
            return new H264SliceHeader(sequence, picture)
            {
                Kind = idr switch { true => H264SliceKind.I, false => H264SliceKind.P },
                IsIdr = idr,
                ReferenceIndicator = 3,
                FrameNumber = number,
                ActiveReferencesL0 = 3
            };
        }
    }
}
