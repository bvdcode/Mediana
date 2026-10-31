// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class YuvResizeOwnershipTests
    {
        [Theory]
        [InlineData(ChromaLocation.Left, false)]
        [InlineData(ChromaLocation.Left, true)]
        [InlineData(ChromaLocation.Center, false)]
        [InlineData(ChromaLocation.Center, true)]
        [InlineData(ChromaLocation.TopLeft, false)]
        [InlineData(ChromaLocation.TopLeft, true)]
        [InlineData(ChromaLocation.Top, false)]
        [InlineData(ChromaLocation.Top, true)]
        [InlineData(ChromaLocation.BottomLeft, false)]
        [InlineData(ChromaLocation.BottomLeft, true)]
        [InlineData(ChromaLocation.Bottom, false)]
        [InlineData(ChromaLocation.Bottom, true)]
        public void IdentityRetainsPlanesAndOriginalBudget(ChromaLocation location, bool padded)
            => YuvResizeOwnershipTestInput.Verify(location, padded);

        [Fact]
        public void ChangedDimensionsAndExplicitCopyKeepTheirDestinationStorage()
        {
            MediaBufferAllocator source = new(4096);
            MediaBufferAllocator destination = new(4096);
            using VideoFrame input = YuvResizeOwnershipTestInput.CreateFrame(ChromaLocation.Left, true, null, source);
            using Yuv420AreaResizer identity = Yuv420AreaResizer.Create(input.Format, 8, 6, destination);
            using VideoFrame copy = identity.Apply(input);
            Assert.False(input.Data.Equals(copy.Data));
            using Yuv420AreaResizer smaller = Yuv420AreaResizer.Create(input.Format, 4, 2, destination);
            using VideoFrame expected = smaller.Apply(input);
            using VideoFrame resized = smaller.ResizeOrRetain(input);
            Assert.Equal(expected.Data.ToArray(), resized.Data.ToArray());
            Assert.Equal(smaller.OutputFormat.CreateTightLayout(), resized.Planes);
            Assert.False(input.Data.Equals(resized.Data));
            input.Dispose();
            Assert.Equal(0, source.AllocatedBytes);
            Assert.NotEmpty(copy.GetRow(0, 0).ToArray());
            Assert.NotEmpty(resized.GetRow(0, 0).ToArray());
        }

        [Fact]
        public void CancellationInvalidMetadataAndDisposalAreCheckedBeforeRetention()
        {
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame input = YuvResizeOwnershipTestInput.CreateFrame(ChromaLocation.Left, false, null, allocator);
            using VideoFrame different = YuvResizeOwnershipTestInput.CreateFrame(ChromaLocation.Center, false, null, allocator);
            using Yuv420AreaResizer resizer = Yuv420AreaResizer.Create(input.Format, 8, 6, allocator);
            long held = allocator.AllocatedBytes;
            Assert.Throws<ArgumentNullException>(() => resizer.ResizeOrRetain(null!));
            Assert.Throws<OperationCanceledException>(() => resizer.ResizeOrRetain(input, new(true)));
            Assert.Throws<NotSupportedException>(() => resizer.ResizeOrRetain(different));
            Assert.Equal(held, allocator.AllocatedBytes);
            different.Dispose();
            input.Dispose();
            Assert.Throws<ObjectDisposedException>(() => resizer.ResizeOrRetain(input));
            resizer.Dispose();
            Assert.Throws<ObjectDisposedException>(() => resizer.ResizeOrRetain(input));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
