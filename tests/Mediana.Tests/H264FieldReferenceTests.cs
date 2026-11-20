// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public partial class H264FieldReferenceTests
    {
        [Fact]
        public void FieldViewsReuseMaterialFramesAndPairedFieldPictures() => H264ReferenceFieldViewTestInput.Verify();

        [Fact]
        public void SlidingWindowCountsPairsAndReleasesBothFieldsTogether()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264ReferenceBuffer buffer = new())
            {
                Store(buffer, allocator, Header(0, false, idr: true));
                long fieldBytes = allocator.AllocatedBytes;
                Store(buffer, allocator, Header(0, true));
                Assert.Equal(2, buffer.Count);
                Assert.Equal(fieldBytes * 2, allocator.AllocatedBytes);
                Store(buffer, allocator, Header(1, false));
                Store(buffer, allocator, Header(1, true));
                Assert.Equal(4, buffer.Count);
                Assert.Equal(fieldBytes * 4, allocator.AllocatedBytes);
                Store(buffer, allocator, Header(2, false));
                Assert.Equal(3, buffer.Count);
                Assert.Equal(fieldBytes * 3, allocator.AllocatedBytes);
                Store(buffer, allocator, Header(2, true));
                IReadOnlyList<H264ReferencePicture?> list = buffer.GetList0(Header(3, false));
                Assert.Equal(new[] { "2T", "2B", "1T", "1B" }, Labels(list));
                Assert.Same(list[0]!.FrameIdentity, list[1]!.FrameIdentity);
                Assert.NotSame(list[0]!.FrameIdentity, list[2]!.FrameIdentity);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void MissingParityUsesNextFrameBeforeOppositeParityOfNewestFrame()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            Store(buffer, allocator, Header(1, true));
            Assert.Equal(new[] { "0T", "1B", "-", "-" }, Labels(buffer.GetList0(Header(2, false))));
            Assert.Equal(new[] { "1B", "0T", "-", "-" }, Labels(buffer.GetList0(Header(2, true))));
        }

        [Fact]
        public void FieldNumbersWrapAndListModificationsCanRepeatAnEarlierEntry()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            for (int number = 0; number <= 16; number++)
            {
                Store(buffer, allocator, Header(number % 16, false, idr: number == 0));
                Store(buffer, allocator, Header(number % 16, true));
            }
            H264SliceHeader header = Header(1, false);
            H264ReferenceNumbering numbering = new(header);
            IReadOnlyList<H264ReferencePicture?> list = buffer.GetList0(header);
            Assert.Equal(new[] { 1, 0, -1, -2 }, list.Select(value => numbering.ShortTerm(value!)));
            header = header with { ReferenceModifications = [new(0, 3), new(1, 31)] };
            Assert.Equal(new[] { "15T", "15T", "0T", "0B" }, Labels(buffer.GetList0(header)));
        }

        [Fact]
        public void BidirectionalListsOrderPairsBeforeAlternatingFieldParity()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true), 0);
            Store(buffer, allocator, Header(0, true), 10);
            Store(buffer, allocator, Header(1, false), 4);
            Store(buffer, allocator, Header(1, true), 5);
            H264SliceHeader header = Header(2, true) with { Kind = H264SliceKind.B, ActiveReferencesL1 = 4 };
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(header, new(0, 2, H264PictureStructure.BottomField));
            Assert.Equal(new[] { "0B", "0T", "1B", "1T" }, Labels(lists.L0));
            Assert.Equal(new[] { "1B", "1T", "0B", "0T" }, Labels(lists.L1));
        }

        [Fact]
        public void BidirectionalIdenticalListsSwapBeforeActiveCountTruncation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            Store(buffer, allocator, Header(0, true));
            H264SliceHeader header = Header(1, false) with { Kind = H264SliceKind.B, ActiveReferencesL0 = 1, ActiveReferencesL1 = 1 };
            H264BidirectionalReferences lists = buffer.GetBidirectionalLists(header, new(4, 0, H264PictureStructure.TopField));
            Assert.Equal(new[] { "0T" }, Labels(lists.L0));
            Assert.Equal(new[] { "0B" }, Labels(lists.L1));
        }

        [Fact]
        public void NonReferencePictureInterruptsPairingWithoutRetainingItsBuffer()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using H264ReferenceBuffer buffer = new();
            Store(buffer, allocator, Header(0, false, idr: true));
            long before = allocator.AllocatedBytes;
            Store(buffer, allocator, Header(1, false) with { ReferenceIndicator = 0 });
            Assert.Equal(before, allocator.AllocatedBytes);
            H264SliceHeader invalid = Header(0, true);
            using H264Picture picture = new(invalid.Sequence, allocator, invalid.Kind, invalid.Structure);
            Assert.Throws<InvalidDataException>(() => buffer.Store(picture, invalid, Order(invalid, 1)));
            Assert.Equal(1, buffer.Count);
        }

        private static string[] Labels(IReadOnlyList<H264ReferencePicture?> list) => list.Select(value => value is null ? "-" :
            value.FrameNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + (value.Structure switch
            {
                H264PictureStructure.TopField => "T",
                H264PictureStructure.BottomField => "B",
                H264PictureStructure.Frame => "F",
                _ => throw new InvalidDataException()
            })).ToArray();

        private static void Store(H264ReferenceBuffer buffer, MediaBufferAllocator allocator, H264SliceHeader header, int? order = null)
        {
            H264Picture picture = new(header.Sequence, allocator, header.Kind, header.Structure);
            bool transferred = false;
            try
            {
                buffer.Store(picture, header, Order(header, order ?? header.FrameNumber * 2 + (header.Structure == H264PictureStructure.BottomField ? 1 : 0)));
                transferred = true;
            }
            finally
            {
                if (!transferred)
                {
                    picture.Dispose();
                }
            }
        }

        private static H264PictureOrder Order(H264SliceHeader header, int value) => header.Structure switch
        {
            H264PictureStructure.TopField => new(value, 0, header.Structure),
            H264PictureStructure.BottomField => new(0, value, header.Structure),
            H264PictureStructure.Frame => new(value, value),
            _ => throw new ArgumentOutOfRangeException(nameof(header))
        };

        private static H264SliceHeader Header(int number, bool bottom, bool idr = false)
        {
            H264SequenceParameterSet sequence = new()
            {
                FrameNumberBits = 4,
                ReferenceFrames = 2,
                WidthInMbs = 1,
                HeightInMapUnits = 1,
                Width = 16,
                Height = 32
            };
            H264PictureParameterSet picture = new(0, 0, false, false, 26, 0, 0, true, false, false, false, 4, 4, false, 0);
            return new H264SliceHeader(sequence, picture)
            {
                Kind = idr ? H264SliceKind.I : H264SliceKind.P,
                IsIdr = idr,
                ReferenceIndicator = 3,
                FrameNumber = number,
                Structure = bottom ? H264PictureStructure.BottomField : H264PictureStructure.TopField,
                ActiveReferencesL0 = 4
            };
        }
    }
}
