// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcPictureStorageTests
    {
        [Theory]
        [InlineData("main8")]
        [InlineData("main10")]
        public async Task PublishedSamplesShareTheReferenceBufferAndOutliveThePicture(string name)
        {
            await HevcPictureStorageTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name);
        }

        [Theory]
        [InlineData("main8")]
        [InlineData("main10")]
        public async Task PublicationFreezesWritablePlanesAndFailedAllocationDoesNotLeak(string name)
        {
            SequenceParameters sequence = await HevcPictureStorageTestInput.ReadSequenceAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name);
            MediaBufferAllocator insufficient = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => new DecodedPicture(sequence, insufficient));
            Assert.Equal(0, insufficient.AllocatedBytes);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (DecodedPicture picture = new(sequence, allocator))
            using (MediaBuffer buffer = picture.PublishPixels())
            {
                if (sequence.BitDepth == 8) { Assert.Throws<InvalidOperationException>(() => picture.Luma.ByteView.Samples.Clear()); }
                else { Assert.Throws<InvalidOperationException>(() => picture.Luma.WordView.Samples.Clear()); }
                Assert.Throws<InvalidOperationException>(() => picture.Luma.Neighbour(0, 0));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void BorrowingAnEmptyPixelBufferCannotStartAnUnbudgetedRental()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SamplePlane(4, 4, 8, Memory<byte>.Empty));
        }
    }
}
