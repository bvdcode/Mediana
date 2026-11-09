// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1InterHeaderTests
    {
        [Fact]
        public void ReadsIndependentInterFrameIdentifiersAndReferenceIndices()
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "interRepeat.obu"));
            IReadOnlyList<ReadOnlyMemory<byte>> units = Av1SequencePictureTests.AccessUnits(encoded);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture key = decoder.Decode(units[0].Span))
            using (Av1ReferencePictures references = new())
            {
                Assert.Equal(1280, key.Header.FrameId);
                Assert.Equal(15, key.Sequence.Tools.FrameIdBits);
                Assert.Equal(14, key.Sequence.Tools.DeltaFrameIdBits);
                Span<ushort> cdfs = stackalloc ushort[Av1DefaultCdfs.Count];
                Av1TileProbabilities.Initialize(cdfs, 0);
                references.Save(key, cdfs, allocator);
                ReadOnlySpan<int> firstReference = [0, 0, 1, 2];
                for (int index = 1; index < units.Count; index++)
                {
                    Av1ObuReader obus = new(units[index].Span);
                    Av1FrameHeader header = default;
                    while (obus.TryRead(out int type, out ReadOnlySpan<byte> payload))
                    {
                        if (type == 6) { header = Av1FrameHeader.Parse(payload, key.Sequence, references); }
                    }
                    Assert.Equal(Av1FrameType.Inter, header.FrameType);
                    Assert.Equal(1280 + index, header.FrameId);
                    Assert.Equal(1 << index, header.RefreshReferences);
                    Assert.Equal(0, header.InvalidReferences);
                    Assert.Equal(firstReference[index - 1], header.Inter.References[0]);
                    Assert.Equal(Av1InterPrediction.NoPrimaryReference, header.Inter.PrimaryReference);
                    Assert.True(header.Inter.HighPrecisionMotion);
                    Assert.False(header.Inter.IntegerMotion);
                    Assert.True(header.ShowFrame);
                    Assert.True(header.Showable);
                    Assert.Equal(23, header.TileOffset);
                    using Av1Picture indexed = new(header, key.Pixels.Retain());
                    references.Save(indexed, cdfs, allocator);
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(16, 1, false)]
        [InlineData(17, 1, false)]
        [InlineData(18, 1, true)]
        [InlineData(1, 31, false)]
        [InlineData(1, 20, false)]
        [InlineData(1, 17, false)]
        [InlineData(1, 16, true)]
        [InlineData(1, 2, true)]
        public void ExpiresReferencesUsingModularFrameIdentifiers(int current, int previous, bool expired)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "yuv16.obu"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (Av1PictureDecoder decoder = new(allocator))
            using (Av1Picture pixels = decoder.Decode(encoded))
            using (Av1ReferencePictures references = new())
            using (Av1Picture indexed = new(pixels.Header with { FrameId = previous }, pixels.Pixels.Retain()))
            {
                Span<ushort> cdfs = stackalloc ushort[Av1DefaultCdfs.Count];
                Av1TileProbabilities.Initialize(cdfs, 0);
                references.Save(indexed, cdfs, allocator);
                Assert.Equal(expired ? Av1ReferencePictures.All : 0, references.Expired(current, 5, 4));
                Assert.Equal(0, references.Expired(current, 0, 0));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
