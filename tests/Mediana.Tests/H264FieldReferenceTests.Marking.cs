// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public partial class H264FieldReferenceTests
    {
        [Fact]
        public void ShortTermRemovalReleasesOnlyTheSelectedField()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            Store(buffer, allocator, Header(0, true));
            long before = allocator.AllocatedBytes;
            Store(buffer, allocator, Header(1, false) with
            {
                AdaptiveMarking = true,
                MemoryOperations = [new(1, DifferenceMinusOne: 2)]
            });
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(new[] { "1T", "0T", "-", "-" }, Labels(buffer.GetList0(Header(2, true))));
            Store(buffer, allocator, Header(1, true));
            Assert.Equal(new[] { "1T", "1B", "0T", "-" }, Labels(buffer.GetList0(Header(2, false))));
        }

        [Fact]
        public void LongTermPairAssignmentKeepsBothFieldsAndRemovalUsesParityNumber()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            Store(buffer, allocator, Header(0, true));
            Store(buffer, allocator, Header(1, false) with
            {
                AdaptiveMarking = true,
                MemoryOperations = [new(4, MaxLongTermFrameIndexPlusOne: 1), new(3, DifferenceMinusOne: 1), new(3, DifferenceMinusOne: 2)]
            });
            H264SliceHeader next = Header(1, true);
            IReadOnlyList<H264ReferencePicture?> list = buffer.GetList0(next);
            Assert.Equal(new[] { "1T", "0B", "0T", "-" }, Labels(list));
            Assert.Equal(new int?[] { null, 0, 0, null }, list.Select(value => value?.LongTermIndex));
            Assert.Equal(new[] { "0T", "1T", "0B", "-" }, Labels(buffer.GetList0(next with { ReferenceModifications = [new(2, 0)] })));
            Store(buffer, allocator, next with { AdaptiveMarking = true, MemoryOperations = [new(2, LongTermPictureNumber: 1)] });
            Assert.Equal(new[] { "1T", "1B", "0T", "-" }, Labels(buffer.GetList0(Header(2, false))));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CurrentAndPreviousFieldsCanBeAssignedLongTermInEitherCommandOrder(bool currentFirst)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            H264MemoryOperation assignPrevious = new(3, DifferenceMinusOne: 0);
            H264MemoryOperation assignCurrent = new(6);
            Store(buffer, allocator, Header(0, true) with
            {
                AdaptiveMarking = true,
                MemoryOperations = currentFirst
                    ? [new(4, MaxLongTermFrameIndexPlusOne: 1), assignCurrent, assignPrevious]
                    : [new(4, MaxLongTermFrameIndexPlusOne: 1), assignPrevious, assignCurrent]
            });
            IReadOnlyList<H264ReferencePicture?> list = buffer.GetList0(Header(1, false));
            Assert.Equal(new[] { "0T", "0B", "-", "-" }, Labels(list));
            Assert.Equal(new int?[] { 0, 0, null, null }, list.Select(value => value?.LongTermIndex));
        }

        [Fact]
        public void IdrFieldLongTermAssignmentPreservesCompanionButReplacesAnOlderPair()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { LongTermReference = true });
            Store(buffer, allocator, Header(0, true) with { AdaptiveMarking = true, MemoryOperations = [new(6)] });
            long pairBytes = allocator.AllocatedBytes;
            Assert.Equal(2, buffer.Count);
            Store(buffer, allocator, Header(1, false) with { AdaptiveMarking = true, MemoryOperations = [new(6)] });
            Assert.Equal(1, buffer.Count);
            Assert.Equal(pairBytes / 2, allocator.AllocatedBytes);
            Assert.Equal(new[] { "1T", "-", "-", "-" }, Labels(buffer.GetList0(Header(2, false))));
        }

        [Fact]
        public void WrappedFrameNumbersDoNotMergeUnrelatedLongTermAndShortTermPairs()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { LongTermReference = true });
            Store(buffer, allocator, Header(0, true) with { AdaptiveMarking = true, MemoryOperations = [new(6)] });
            for (int number = 1; number <= 16; number++)
            {
                Store(buffer, allocator, Header(number % 16, false));
                Store(buffer, allocator, Header(number % 16, true));
            }
            IReadOnlyList<H264ReferencePicture?> list = buffer.GetList0(Header(1, false));
            Assert.Equal(new[] { "0T", "0B", "0T", "0B" }, Labels(list));
            Assert.Equal(new int?[] { null, null, 0, 0 }, list.Select(value => value?.LongTermIndex));
            Assert.NotSame(list[0]!.FrameIdentity, list[2]!.FrameIdentity);
        }

        [Fact]
        public void ResetRenumbersTheFirstFieldBeforePairingTheNext()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            Store(buffer, allocator, Header(0, true));
            Store(buffer, allocator, Header(1, false) with { AdaptiveMarking = true, MemoryOperations = [new(5)] }, 0);
            Store(buffer, allocator, Header(0, true));
            Assert.Equal(new[] { "0T", "0B", "-", "-" }, Labels(buffer.GetList0(Header(1, false))));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RemovingFirstLongTermFieldDoesNotWaiveAssignmentOfItsCompanion(bool removeFirst)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { LongTermReference = true });
            H264SliceHeader invalid = Header(0, true) with
            {
                AdaptiveMarking = true,
                MemoryOperations = removeFirst ? [new(2, LongTermPictureNumber: 0)] : []
            };
            using H264Picture input = new(invalid.Sequence, allocator, invalid.Kind, invalid.Structure);
            long before = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => buffer.Store(input, invalid, Order(invalid, 1)));
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(new[] { "0T", "-", "-", "-" }, Labels(buffer.GetList0(Header(1, false))));
        }

        [Fact]
        public void IncompletePairConversionIsRejectedEvenIfTheConvertedFieldIsRemovedLater()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            Store(buffer, allocator, Header(0, true));
            H264SliceHeader invalid = Header(1, false) with
            {
                AdaptiveMarking = true,
                MemoryOperations = [new(4, MaxLongTermFrameIndexPlusOne: 1), new(3, DifferenceMinusOne: 1), new(2, LongTermPictureNumber: 1)]
            };
            using H264Picture input = new(invalid.Sequence, allocator, invalid.Kind, invalid.Structure);
            long before = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => buffer.Store(input, invalid, Order(invalid, 2)));
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(new[] { "0T", "0B", "-", "-" }, Labels(buffer.GetList0(Header(1, false))));
        }
    }
}
