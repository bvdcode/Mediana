// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public partial class H264FieldReferenceTests
    {
        [Fact]
        public void FrameProvidesDistinctStableFieldReferencesWithoutAnotherPixelBuffer()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { Structure = H264PictureStructure.Frame });
            long before = allocator.AllocatedBytes;
            IReadOnlyList<H264ReferencePicture?> top = buffer.GetList0(Header(1, false));
            IReadOnlyList<H264ReferencePicture?> bottom = buffer.GetList0(Header(1, true));
            Assert.Equal(new[] { "0T", "0B", "-", "-" }, Labels(top));
            Assert.Equal(new[] { "0B", "0T", "-", "-" }, Labels(bottom));
            Assert.Same(top[0]!.Picture, top[1]!.Picture);
            Assert.Same(top[0]!.Identity, bottom[1]!.Identity);
            Assert.NotSame(top[0]!.Identity, top[1]!.Identity);
            Assert.Same(top[0]!.Identity.Frame, top[1]!.Identity.Frame);
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(1, buffer.Count);
        }

        [Fact]
        public void FieldListModificationDoesNotDiscardTheOtherHalfOfTheSameBuffer()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { Structure = H264PictureStructure.Frame });
            Store(buffer, allocator, Header(1, false) with { Structure = H264PictureStructure.Frame });
            H264SliceHeader header = Header(2, false) with { ReferenceModifications = [new(0, 3)] };
            Assert.Equal(new[] { "0T", "1T", "1B", "0B" }, Labels(buffer.GetList0(header)));
            Assert.Equal(new[] { "0B", "1T", "1B", "0T" }, Labels(buffer.GetList0(header with { ReferenceModifications = [new(0, 4)] })));
            Assert.Equal(new[] { "0B" }, Labels(buffer.GetList0(header with { ActiveReferencesL0 = 1, ReferenceModifications = [new(0, 4)] })));
        }

        [Fact]
        public void MarkingOneFrameFieldKeepsItsPhysicalBufferUntilTheOtherFieldIsReleased()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer buffer = new())
            {
                Store(buffer, allocator, Header(0, false, idr: true) with { Structure = H264PictureStructure.Frame });
                long frameBytes = allocator.AllocatedBytes;
                Store(buffer, allocator, Header(1, false) with
                {
                    AdaptiveMarking = true,
                    MemoryOperations = [new(1, DifferenceMinusOne: 2)]
                });
                long fieldBytes = allocator.AllocatedBytes - frameBytes;
                Assert.True(fieldBytes > 0);
                Assert.Equal(new[] { "1T", "0T", "-", "-" }, Labels(buffer.GetList0(Header(2, false))));
                Assert.All(buffer.GetList0(Header(2, false) with { Structure = H264PictureStructure.Frame }), Assert.Null);
                Store(buffer, allocator, Header(1, true) with
                {
                    AdaptiveMarking = true,
                    MemoryOperations = [new(1, DifferenceMinusOne: 2)]
                });
                Assert.Equal(fieldBytes * 2, allocator.AllocatedBytes);
                Assert.Equal(new[] { "1T", "1B", "-", "-" }, Labels(buffer.GetList0(Header(2, false))));
                H264ReferencePicture frame = Assert.IsType<H264ReferencePicture>(buffer.GetList0(Header(2, false) with
                {
                    Structure = H264PictureStructure.Frame
                })[0]);
                Assert.Equal(H264PictureStructure.Frame, frame.Structure);
                Assert.NotNull(frame.Companion);
                Assert.NotSame(frame.Picture, frame.Companion);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailedFieldMarkingDoesNotSplitOrReleaseTheOriginalFrame()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { Structure = H264PictureStructure.Frame });
            H264SliceHeader invalid = Header(1, false) with
            {
                AdaptiveMarking = true,
                MemoryOperations = [new(1, DifferenceMinusOne: 2), new(2, LongTermPictureNumber: 0)]
            };
            using H264Picture picture = new(invalid.Sequence, allocator, invalid.Kind, invalid.Structure);
            long before = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => buffer.Store(picture, invalid, Order(invalid, 2)));
            Assert.Equal(1, buffer.Count);
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(new[] { "0T", "0B", "-", "-" }, Labels(buffer.GetList0(Header(1, false))));
        }

        [Fact]
        public void FrameMarkingCannotRemoveAnUnpairedReferenceField()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            H264SliceHeader invalid = Header(1, false) with
            {
                Structure = H264PictureStructure.Frame,
                AdaptiveMarking = true,
                MemoryOperations = [new(1, DifferenceMinusOne: 0)]
            };
            using H264Picture picture = new(invalid.Sequence, allocator, invalid.Kind, invalid.Structure);
            long before = allocator.AllocatedBytes;
            Assert.Throws<InvalidDataException>(() => buffer.Store(picture, invalid, Order(invalid, 2)));
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(new[] { "0T", "-", "-", "-" }, Labels(buffer.GetList0(Header(1, false))));
        }

        [Fact]
        public void FrameListsCollapseMaterialFrameFieldsWithoutCopyingPixels()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true) with { Structure = H264PictureStructure.Frame });
            Store(buffer, allocator, Header(1, false));
            long before = allocator.AllocatedBytes;
            H264ReferencePicture frame = Assert.IsType<H264ReferencePicture>(buffer.GetList0(Header(2, false) with
            {
                Structure = H264PictureStructure.Frame
            })[0]);
            Assert.Equal(0, frame.FrameNumber);
            Assert.Equal(H264PictureStructure.Frame, frame.Structure);
            Assert.Equal(H264PictureStructure.Frame, frame.Picture.Structure);
            Assert.Null(frame.Companion);
            Assert.Same(frame.FrameIdentity, frame.Identity);
            Assert.Equal(before, allocator.AllocatedBytes);
        }
    }
}
