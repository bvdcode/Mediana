// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcReferenceBufferTests
    {
        [Fact]
        public void TemporalStoragePreservesRoundedSixteenSampleCellsWithMuchSmallerCapacity()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (HevcMotionField current = new(72, 40, allocator))
            {
                for (int y = 0; y < 40; y += 4)
                {
                    for (int x = 0; x < 72; x += 4)
                    { current.Set(x, y, 4, 4, new(x, y, new((short)x, (short)y), default, 0, -1, HevcMotionSample.List0)); }
                }
                long workingBytes = allocator.AllocatedBytes;
                using HevcMotionField compact = current.Compact(allocator);
                Assert.Equal(5 * 3 * Unsafe.SizeOf<HevcMotionSample>(), allocator.AllocatedBytes - workingBytes);
                current.Dispose();
                for (int y = 0; y < 40; y++)
                {
                    for (int x = 0; x < 72; x++)
                    {
                        HevcMotionSample actual = compact.Get(x, y);
                        Assert.True(actual.IsAvailable);
                        Assert.Equal(x / 16 * 16, actual.PictureOrder0);
                        Assert.Equal(y / 16 * 16, actual.PictureOrder1);
                    }
                }
                Assert.False(compact.Get(-1, 0).IsAvailable);
                Assert.Throws<InvalidOperationException>(() => compact.Set(0, 0, 4, 4, default));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ReferenceListsUsePocOrderRepetitionAndExplicitModifications()
        {
            SequenceParameters sequence = await HevcPictureStorageTestInput.ReadSequenceAsync(Path.Combine(AppContext.BaseDirectory, "Data"), "main8");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (HevcReferenceBuffer buffer = new())
            {
                foreach (int order in new[] { -1, 1, 2, 3, 5, 9 }) { buffer.Store(Picture(sequence, order, allocator), 16); }
                HevcSliceReferences syntax = new(4, new([new(-3, true), new(1, true), new(-2, true), new(5, false)]), [new(255, true)]);
                HevcReferenceLists lists = buffer.Select(syntax, 4, 8).CreateLists(7, 6, [], []);
                Assert.Equal(new[] { 2, 1, 5, -1, 2, 1, 5 }, lists.List0.Select(entry => entry.Picture.Order));
                Assert.Equal(new[] { 5, 2, 1, -1, 5, 2 }, lists.List1.Select(entry => entry.Picture.Order));
                Assert.True(lists.List0[3].LongTerm);
                Assert.Equal(5, buffer.Count);
                HevcReferenceLists modified = buffer.Select(syntax, 4, 8).CreateLists(3, 2, [3, 1, 0], [2, 3]);
                Assert.Equal(new[] { -1, 1, 2 }, modified.List0.Select(entry => entry.Picture.Order));
                Assert.Equal(new[] { 1, -1 }, modified.List1.Select(entry => entry.Picture.Order));
                Assert.Throws<InvalidDataException>(() => buffer.Select(new(4, new([new(-1, true)]), []), 4, 8));
                Assert.Equal(5, buffer.Count);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task LongTermMsbCyclesSelectTheExactWrapAndUnlistedReferencesAreReleased()
        {
            SequenceParameters sequence = await HevcPictureStorageTestInput.ReadSequenceAsync(Path.Combine(AppContext.BaseDirectory, "Data"), "main8");
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (HevcReferenceBuffer buffer = new())
            {
                buffer.Store(Picture(sequence, 5, allocator), 16); buffer.Store(Picture(sequence, 261, allocator), 16);
                Assert.Throws<InvalidDataException>(() => buffer.Select(new(10, ShortTermReferenceSet.Empty, [new(5, true)]), 522, 8));
                HevcReferenceLists lists = buffer.Select(new(10, ShortTermReferenceSet.Empty, [new(5, true, true, 2)]), 522, 8).CreateLists(1, 0, [], []);
                Assert.Equal(5, Assert.Single(lists.List0).Picture.Order);
                Assert.True(lists.List0[0].LongTerm); Assert.Equal(1, buffer.Count);
                buffer.Select(new(11, new([new(-10, false)]), []), 523, 8);
                Assert.Equal(0, buffer.Count);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("main8")]
        [InlineData("main10")]
        [InlineData("weighted8")]
        [InlineData("weighted10")]
        public async Task EveryIndependentVideoReferenceSetResolvesThroughReorderedPictures(string name)
        {
            HevcSliceHeader[] headers = await HevcSliceHeaderTestInput.ReadHeadersAsync(Path.Combine(AppContext.BaseDirectory, "Data"), name);
            MediaBufferAllocator allocator = new(32 * 1024 * 1024);
            HevcPictureOrderCounter counter = new(headers[0].Picture.Sequence.ReferencePictures.Log2PictureOrderCount);
            using (HevcReferenceBuffer buffer = new())
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    HevcSliceHeader slice = headers[i];
                    int order = counter.Derive(slice.NalType, slice.TemporalId, slice.PictureOrderCount, i == 0);
                    HevcReferenceLists lists = buffer.Select(slice.References, order, slice.Picture.Sequence.ReferencePictures.Log2PictureOrderCount).CreateLists(slice);
                    Assert.Equal(slice.ActiveReferences0, lists.List0.Count); Assert.Equal(slice.ActiveReferences1, lists.List1.Count);
                    foreach (HevcReferenceEntry entry in lists.List0.Concat(lists.List1)) { Assert.NotEqual(order, entry.Picture.Order); }
                    if (slice.NalType is >= 16 and <= 21 || (slice.NalType & 1) != 0)
                    { buffer.Store(Picture(slice.Picture.Sequence, order, allocator), slice.Picture.Sequence.MaximumDecodedPictures); }
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static HevcReferencePicture Picture(SequenceParameters sequence, int order, MediaBufferAllocator allocator)
        {
            DecodedPicture pixels = new(sequence, allocator);
            using HevcMotionField current = new(sequence.Width, sequence.Height, allocator);
            bool completed = false;
            try
            {
                HevcReferencePicture result = new(pixels, order, current.Compact(allocator));
                completed = true; return result;
            }
            finally { if (!completed) { pixels.Dispose(); } }
        }
    }
}
