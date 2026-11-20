// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264PublicFieldTests
    {
        [Fact]
        public Task PreservesIndependentFieldChromaLocationsAcrossDecodingAndSeek() =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor("avc-paff-field-chroma"),
                VideoPixelFormat.Yuv420P8, 96, 96, 12, 12,
                Enumerable.Repeat(VideoScanMode.TopFieldFirst, 12).ToArray(),
                new(ChromaLocation.TopLeft, ChromaLocation.BottomLeft));

        [Theory]
        [InlineData("avc-paff-cabac", 8, 2, 12)]
        [InlineData("avc-paff-cavlc", 8, 2, 12)]
        [InlineData("avc-paff-b-cabac", 8, 2, 12)]
        [InlineData("avc-paff-b-cavlc", 8, 2, 12)]
        [InlineData("avc-paff-b-cabac-temporal", 8, 2, 12)]
        [InlineData("avc-paff-b-cavlc-temporal", 8, 2, 12)]
        [InlineData("avc-paff-high10-cabac", 10, 2, 12)]
        [InlineData("avc-paff-high422-cavlc", 8, 1, 12)]
        [InlineData("avc-paff-high422-10-cabac", 10, 1, 12)]
        [InlineData("avc-mixed-cabac", 8, 2, 24)]
        [InlineData("avc-mixed-cavlc", 8, 2, 24)]
        [InlineData("avc-mixed-b-cabac-spatial", 8, 2, 24)]
        [InlineData("avc-mixed-b-cavlc-spatial", 8, 2, 24)]
        [InlineData("avc-mixed-b-cabac-temporal", 8, 2, 24)]
        [InlineData("avc-mixed-b-cavlc-temporal", 8, 2, 24)]
        public Task DecodesEveryFrameAndSeeksBackwardThroughFieldPairs(string name, int depth, int vertical, int count)
        {
            VideoScanMode[] scans = Enumerable.Range(0, count).Select(index => count switch
            {
                12 => VideoScanMode.TopFieldFirst,
                24 when index is >= 8 and < 17 => VideoScanMode.TopFieldFirst,
                24 => VideoScanMode.Progressive,
                _ => throw new ArgumentOutOfRangeException(nameof(count))
            }).ToArray();
            return H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name),
                new PlanarVideoPixelFormat("reference-yuv", PlanarColorModel.Yuv, depth, 2, vertical), 96, 96, count, 12, scans);
        }
    }
}
