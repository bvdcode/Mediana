// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoFieldChromaTests
    {
        [Theory]
        [InlineData(ChromaLocation.Left, 0, 1, 3)]
        [InlineData(ChromaLocation.Center, 2, 1, 3)]
        [InlineData(ChromaLocation.TopLeft, 0, 0, 2)]
        [InlineData(ChromaLocation.Top, 2, 0, 2)]
        [InlineData(ChromaLocation.BottomLeft, 0, 2, 4)]
        [InlineData(ChromaLocation.Bottom, 2, 2, 4)]
        public void FieldOffsetsFollowTheFullFrameSamplingGrid(ChromaLocation location, int horizontal, int top, int bottom)
        {
            Assert.Equal((horizontal, top), ChromaSamplePosition.InQuarterFieldLumaSamples(location, VideoFieldParity.Top));
            Assert.Equal((horizontal, bottom), ChromaSamplePosition.InQuarterFieldLumaSamples(location, VideoFieldParity.Bottom));
        }

        [Theory]
        [InlineData(VideoScanMode.TopFieldFirst)]
        [InlineData(VideoScanMode.BottomFieldFirst)]
        public void InterpolationUsesOnlyTheCurrentFieldsSamples(VideoScanMode mode)
        {
            VideoFormat format = new(2, 8, VideoPixelFormat.Yuv420P8, null, null, null, mode);
            PlaneLayout[] planes = format.CreateTightLayout();
            MediaBufferAllocator allocator = new(1024);
            using WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset);
            new byte[] { 0, 240, 80, 160 }.CopyTo(writable.Memory.Span[planes[1].Offset..]);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(format, buffer, planes);
            double[] expected = [0, 240, 30, 230, 70, 190, 80, 160];
            for (int row = 0; row < expected.Length; row++)
            {
                BilinearChromaRow<byte> chroma = new(frame, 1, row, ChromaLocation.Left);
                Assert.Equal(expected[row], chroma.Sample(0));
            }
            VideoFieldChromaLocations locations = new(ChromaLocation.TopLeft, ChromaLocation.BottomLeft);
            BilinearChromaRow<byte> top = new(frame, 1, 2, ChromaLocation.Unspecified, locations);
            BilinearChromaRow<byte> bottom = new(frame, 1, 3, ChromaLocation.Unspecified, locations);
            Assert.Equal(40, top.Sample(0));
            Assert.Equal(240, bottom.Sample(0));
        }

        [Theory]
        [InlineData(VideoScanMode.TopField, 30)]
        [InlineData(VideoScanMode.BottomField, 10)]
        public void AStandaloneFieldUsesItsOwnSampleGrid(VideoScanMode mode, double expected)
        {
            VideoFormat format = new(2, 4, VideoPixelFormat.Yuv420P8, null, null, null, mode);
            PlaneLayout[] planes = format.CreateTightLayout();
            MediaBufferAllocator allocator = new(1024);
            using WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset);
            new byte[] { 0, 80 }.CopyTo(writable.Memory.Span[planes[1].Offset..]);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(format, buffer, planes);
            BilinearChromaRow<byte> row = new(frame, 1, 1, ChromaLocation.Left);
            Assert.Equal(expected, row.Sample(0));
        }

        [Fact]
        public void CicpPrecedencePreservesIndependentFieldSampling()
        {
            VideoColorInfo codec = new(ChromaLocation: ChromaLocation.Unspecified)
            {
                FieldChromaLocations = new(ChromaLocation.TopLeft, ChromaLocation.BottomLeft)
            };
            VideoColorInfo resolved = new CicpColorDescription(1, 1, 1, true).Resolve(codec);
            Assert.Equal(codec.FieldChromaLocations, resolved.FieldChromaLocations);
            Assert.Equal(ChromaLocation.Unspecified, resolved.ChromaLocation);
            Assert.Equal(ColorRange.Full, resolved.Range);
            Assert.Equal(ColorPrimaries.Bt709, resolved.Primaries);
        }
    }
}
