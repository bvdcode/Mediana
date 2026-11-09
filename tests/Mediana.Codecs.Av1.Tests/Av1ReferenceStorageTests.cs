// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1ReferenceStorageTests
    {
        [Fact]
        public void LoadingPrimaryReferenceResetsCountsAndPreservesProbabilities()
        {
            ushort[] saved = Av1DefaultCdfs.Values.ToArray();
            foreach (int offset in Av1DefaultCdfs.CounterOffsets) { saved[offset] = 32; }
            Span<ushort> loaded = stackalloc ushort[Av1DefaultCdfs.Count];
            Av1TileProbabilities.Initialize(loaded, 200, saved);
            Assert.True(loaded.SequenceEqual(Av1DefaultCdfs.Values));
            foreach (int offset in Av1DefaultCdfs.CounterOffsets) { Assert.Equal(32, saved[offset]); }
        }

        [Fact]
        public void SharedSlotsOwnOneFrozenProbabilitySnapshotAndOnePixelLease()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "yuv16.obu"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture picture = decoder.Decode(encoded))
            {
                long pixelBytes = allocator.AllocatedBytes;
                Span<ushort> probabilities = stackalloc ushort[Av1DefaultCdfs.Count];
                Av1TileProbabilities.Initialize(probabilities, 120);
                ushort expected = probabilities[Av1DefaultCdfs.CoeffBaseOffset];
                using (Av1ReferencePictures references = new())
                {
                    references.Save(picture, probabilities, allocator);
                    Assert.Equal(pixelBytes + probabilities.Length * sizeof(ushort), allocator.AllocatedBytes);
                    Assert.Same(references.Get(0), references.Get(7));
                    probabilities.Clear();
                    ReadOnlySpan<ushort> snapshot = MemoryMarshal.Cast<byte, ushort>(references.Get(7).Cdfs.Memory.Span);
                    Assert.Equal(expected, snapshot[Av1DefaultCdfs.CoeffBaseOffset]);
                    Av1TileProbabilities.Initialize(probabilities, 0, snapshot);
                    Assert.True(probabilities.SequenceEqual(snapshot));
                    references.Invalidate(1);
                    Assert.Throws<InvalidDataException>(() => references.Get(0));
                    Assert.Equal(expected, MemoryMarshal.Cast<byte, ushort>(references.Get(7).Cdfs.Memory.Span)[Av1DefaultCdfs.CoeffBaseOffset]);
                    references.Invalidate(Av1ReferencePictures.All);
                    Assert.Equal(pixelBytes, allocator.AllocatedBytes);
                }
                Assert.Equal(pixelBytes, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
