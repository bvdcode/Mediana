// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Tests;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1SuperResolutionPictureTests
    {
        [Fact]
        public void MatchesIndependentPicturesAcrossScalesTilesRestorationAndGrain() =>
            Av1SuperResolutionTestInput.VerifyPictures(Root);

        [Fact]
        public void HeldUpscaledFramesSurviveReferenceUpdatesResetAndCancellation() =>
            Av1SuperResolutionTestInput.VerifyOwnership(Root);

        [Fact]
        public void SeparatesCodedReferenceMapsFromUpscaledPixelsAcrossFrameScaleChanges()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            HashSet<int> denominators = [];
            using (Av1PictureDecoder decoder = new(allocator))
            {
                foreach (ReadOnlyMemory<byte> packet in Packets("superresRandom"))
                {
                    using Av1Picture picture = decoder.Decode(packet.Span);
                    Av1FrameHeader header = picture.Header;
                    Assert.Equal(192, header.PixelGeometry.Width);
                    if (header.SuperResolution.Active) { Assert.Equal(header.SuperResolution.CodedWidth, header.Geometry.Width); }
                    else { Assert.Equal(header.PixelGeometry.Width, header.Geometry.Width); }
                    Assert.Equal(header.PixelGeometry.SampleCount, picture.Pixels.Memory.Length);
                    Assert.Equal(2 * ((header.Geometry.Width + 7) >> 3), header.Geometry.MiColumns);
                    denominators.Add(header.SuperResolution.Denominator);
                }
            }
            Assert.True(denominators.Count > 1);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void BudgetFailureReleasesCodedUpscaledAndIntermediateStorage(int stage)
        {
            ReadOnlyMemory<byte> packet = Packets("superres9")[0];
            MediaBufferAllocator parent = new(16 * 1024 * 1024);
            Av1FrameHeader header;
            using (Av1PictureDecoder probe = new(parent))
            using (Av1Picture picture = probe.Decode(packet.Span)) { header = picture.Header; }
            long limit = stage switch
            {
                0 => header.Geometry.SampleCount - 1,
                1 => header.Geometry.SampleCount,
                2 => header.Geometry.SampleCount + header.PixelGeometry.SampleCount,
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
            MediaBufferAllocator scope = parent.CreateScope(limit);
            Assert.Throws<MediaMemoryLimitException>(() => Decode(packet, scope));
            Assert.Equal(0, scope.AllocatedBytes);
            Assert.Equal(0, parent.AllocatedBytes);
        }

        private static string Root => Path.Combine(AppContext.BaseDirectory, "Data");
        private static List<ReadOnlyMemory<byte>> Packets(string name) =>
            Av1PictureSequenceTestInput.Packets(File.ReadAllBytes(Path.Combine(Root, name + ".obu")));

        private static void Decode(ReadOnlyMemory<byte> packet, MediaBufferAllocator allocator)
        {
            using Av1PictureDecoder decoder = new(allocator);
            using Av1Picture picture = decoder.Decode(packet.Span);
        }
    }
}
