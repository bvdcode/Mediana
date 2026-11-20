// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264FieldOutputTests
    {
        [Theory]
        [InlineData(1, 8, false)]
        [InlineData(1, 10, true)]
        [InlineData(2, 8, true)]
        [InlineData(2, 10, false)]
        public void CroppedPairsPreserveSamplesPresentationOrderAndExactDuration(int chroma, int depth, bool bottomFirst)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (H264FrameQueue queue = new(allocator))
            {
                H264SliceHeader top = Header(H264PictureStructure.TopField, chroma, depth);
                H264SliceHeader bottom = top with { Structure = H264PictureStructure.BottomField };
                using VideoFrame a = Field(top, allocator, 0, new(1, new(1, 60)));
                using VideoFrame b = Field(bottom, allocator, 1, new(10, new(1, 600)));
                long before = allocator.AllocatedBytes;
                queue.Add(a.Retain(), new(bottomFirst ? 1 : 0, 0, top.Structure), top);
                Assert.Null(queue.Receive());
                Assert.Equal(before, allocator.AllocatedBytes);
                queue.Add(b.Retain(), new(0, bottomFirst ? 0 : 1, bottom.Structure), bottom);
                using VideoFrame output = Assert.IsType<VideoFrame>(queue.Receive());
                Assert.Equal(bottomFirst ? VideoScanMode.BottomFieldFirst : VideoScanMode.TopFieldFirst, output.Format.ScanMode);
                Assert.Equal(top.Sequence.Height, output.Format.Height);
                Assert.Equal(new MediaTimestamp(bottomFirst ? 1 : 0, new(1, 60)), output.Timestamp);
                Assert.Equal(new MediaDuration(20, new(1, 600)), output.Duration);
                Assert.Equal(new VideoFieldChromaLocations(ChromaLocation.TopLeft,
                    chroma == 1 ? ChromaLocation.BottomLeft : ChromaLocation.TopLeft), output.Format.Color.FieldChromaLocations);
                for (int plane = 0; plane < output.Planes.Count; plane++)
                {
                    for (int row = 0; row < a.Planes[plane].Height; row++)
                    {
                        Assert.True(output.GetRow(plane, row * 2).SequenceEqual(a.GetRow(plane, row)));
                        Assert.True(output.GetRow(plane, row * 2 + 1).SequenceEqual(b.GetRow(plane, row)));
                    }
                }
                queue.Dispose();
                Assert.NotEmpty(output.Data.ToArray());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void FailedCombinationPreservesBothInputLeasesAndCanBeRetried()
        {
            MediaBufferAllocator allocator = new(16384);
            using (H264FrameQueue queue = new(allocator))
            {
                H264SliceHeader top = Header(H264PictureStructure.TopField);
                H264SliceHeader bottom = top with { Structure = H264PictureStructure.BottomField };
                queue.Add(Field(top, allocator, 0), new(0, 0, top.Structure), top);
                using VideoFrame second = Field(bottom, allocator, 1);
                long before = allocator.AllocatedBytes;
                using (WritableMediaBuffer blocker = allocator.Allocate(checked((int)(allocator.LimitBytes - before))))
                {
                    Assert.Throws<MediaMemoryLimitException>(() => queue.Add(second, new(0, 1, bottom.Structure), bottom));
                    Assert.Equal(1, queue.PendingCount);
                    Assert.Equal(allocator.LimitBytes, allocator.AllocatedBytes);
                    Assert.NotEmpty(second.Data.ToArray());
                }
                Assert.Throws<OperationCanceledException>(() => queue.Add(second, new(0, 1, bottom.Structure), bottom, new(true)));
                Assert.Equal(before, allocator.AllocatedBytes);
                queue.Add(second.Retain(), new(0, 1, bottom.Structure), bottom);
                using VideoFrame paired = Assert.IsType<VideoFrame>(queue.Receive());
                Assert.Null(paired.Duration);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AnIncompletePairIsEmittedAsItsActualFieldOrReleasedOnDisposal(bool drain)
        {
            MediaBufferAllocator allocator = new(16384);
            using (H264FrameQueue queue = new(allocator))
            {
                H264SliceHeader header = Header(H264PictureStructure.BottomField);
                queue.Add(Field(header, allocator, 17), new(0, 0, header.Structure), header);
                Assert.Null(queue.Receive());
                if (drain)
                {
                    queue.Drain();
                    using VideoFrame field = Assert.IsType<VideoFrame>(queue.Receive());
                    Assert.Equal(VideoScanMode.BottomField, field.Format.ScanMode);
                    Assert.Equal(header.Sequence.Height / 2, field.Format.Height);
                    Assert.Equal(new MediaTimestamp(17, new(1, 60)), field.Timestamp);
                    Assert.Null(field.Duration);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ResetOnSecondFieldDoesNotJoinDifferentOrderEpochs()
        {
            MediaBufferAllocator allocator = new(16384);
            using (H264FrameQueue queue = new(allocator))
            {
                H264SliceHeader top = Header(H264PictureStructure.TopField);
                H264SliceHeader bottom = top with { Structure = H264PictureStructure.BottomField, MemoryOperations = [new(5)] };
                queue.Add(Field(top, allocator, 0), new(0, 0, top.Structure), top);
                queue.Add(Field(bottom, allocator, 1), new(0, 0, bottom.Structure), bottom);
                using VideoFrame first = Assert.IsType<VideoFrame>(queue.Receive());
                Assert.Equal(VideoScanMode.TopField, first.Format.ScanMode);
                queue.Drain();
                using VideoFrame second = Assert.IsType<VideoFrame>(queue.Receive());
                Assert.Equal(VideoScanMode.BottomField, second.Format.ScanMode);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void AnIdrCanDiscardAnIncompletePriorPair()
        {
            MediaBufferAllocator allocator = new(16384);
            using (H264FrameQueue queue = new(allocator))
            {
                H264SliceHeader top = Header(H264PictureStructure.TopField);
                queue.Add(Field(top, allocator, 0), new(0, 0, top.Structure), top);
                H264SliceHeader idr = top with { IsIdr = true, NoOutputOfPriorPictures = true };
                queue.Add(Field(idr, allocator, 10), new(0, 0, top.Structure), idr);
                H264SliceHeader bottom = top with { Structure = H264PictureStructure.BottomField };
                queue.Add(Field(bottom, allocator, 11), new(0, 1, bottom.Structure), bottom);
                using VideoFrame combined = Assert.IsType<VideoFrame>(queue.Receive());
                Assert.Equal(new MediaTimestamp(10, new(1, 60)), combined.Timestamp);
                Assert.Null(queue.Receive());
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Field(H264SliceHeader header, MediaBufferAllocator allocator, int timestamp, MediaDuration? duration = null)
        {
            using H264Picture picture = new(header.Sequence, allocator, H264SliceKind.I, header.Structure);
            for (int plane = 0; plane < 3; plane++)
            {
                Span<byte> samples = picture.Plane(plane);
                for (int index = 0; index < samples.Length; index++)
                {
                    samples[index] = (byte)((index + plane * 17 + timestamp * 5) % 256);
                    if (picture.SampleBytes == 2 && (index & 1) != 0) { samples[index] &= 3; }
                }
            }
            return H264FramePublisher.Publish(picture, new(timestamp, new(1, 60)), duration, default);
        }

        private static H264SliceHeader Header(H264PictureStructure structure, int chroma = 1, int depth = 8) => new(new()
        {
            Profile = 122,
            Level = 30,
            WidthInMbs = 1,
            HeightInMapUnits = 1,
            Width = 12,
            Height = 28,
            CropLeft = 2,
            CropTop = 4,
            ChromaFormat = chroma,
            LumaBitDepth = depth,
            ChromaBitDepth = depth,
            PictureOrderType = 2,
            Vui = new() { ChromaLocationTop = 2, ChromaLocationBottom = 4 }
        }, new(0, 0, false, false, 26, 0, 0, true, false, false, false, 1, 1, false, 0))
        {
            Structure = structure,
            ReferenceIndicator = 1
        };
    }
}
