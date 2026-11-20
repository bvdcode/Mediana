// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264MbaffPredictiveTests
    {
        [Theory]
        [InlineData("avc-mbaff-b-cavlc-spatial-field", false)]
        [InlineData("avc-mbaff-b-cavlc-spatial-adaptive", false)]
        [InlineData("avc-mbaff-b-cavlc-spatial-high10", false)]
        [InlineData("avc-mbaff-b-cavlc-spatial-422-10", false)]
        [InlineData("avc-mbaff-b-cavlc-spatial-422", false)]
        [InlineData("avc-mbaff-b-cavlc-temporal-field", false)]
        [InlineData("avc-mbaff-b-cavlc-temporal-adaptive", false)]
        [InlineData("avc-mbaff-b-cavlc-temporal-high10", false)]
        [InlineData("avc-mbaff-b-cavlc-temporal-422-10", false)]
        [InlineData("avc-mbaff-b-cavlc-temporal-422", false)]
        [InlineData("avc-mbaff-b-cabac-spatial-field", true)]
        [InlineData("avc-mbaff-b-cabac-spatial-adaptive", true)]
        [InlineData("avc-mbaff-b-cabac-spatial-high10", true)]
        [InlineData("avc-mbaff-b-cabac-spatial-422-10", true)]
        [InlineData("avc-mbaff-b-cabac-spatial-422", true)]
        [InlineData("avc-mbaff-b-cabac-temporal-field", true)]
        [InlineData("avc-mbaff-b-cabac-temporal-adaptive", true)]
        [InlineData("avc-mbaff-b-cabac-temporal-high10", true)]
        [InlineData("avc-mbaff-b-cabac-temporal-422-10", true)]
        [InlineData("avc-mbaff-b-cabac-temporal-422", true)]
        public async Task BidirectionalMacroblockPairsMatchIndependentDecoder(string name, bool cabac)
        {
            (int frames, int fields) = await H264MbaffSequenceTestInput.VerifyPredictiveAsync(
                Path.GetDirectoryName(Mp4TestData.PathFor(name))!, name, 12, cabac, [0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 11]);
            Assert.Equal(216, frames + fields);
            Assert.True(fields > 0);
            VideoPixelFormat pixels = name switch
            {
                string value when value.EndsWith("422-10", StringComparison.Ordinal) => VideoPixelFormat.Yuv422P10,
                string value when value.EndsWith("high10", StringComparison.Ordinal) => VideoPixelFormat.Yuv420P10,
                string value when value.EndsWith("422", StringComparison.Ordinal) => VideoPixelFormat.Yuv422P8,
                string value when value.EndsWith("field", StringComparison.Ordinal) || value.EndsWith("adaptive", StringComparison.Ordinal) => VideoPixelFormat.Yuv420P8,
                _ => throw new InvalidDataException("Invalid H.264 test pixel format.")
            };
            await H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), pixels, 96, 96, 12, 12,
                Enumerable.Repeat(VideoScanMode.TopFieldFirst, 12).ToArray());
        }

        [Theory]
        [InlineData("avc-mbaff-filter-cavlc-spatial-field", false)]
        [InlineData("avc-mbaff-filter-cavlc-spatial-adaptive", false)]
        [InlineData("avc-mbaff-filter-cavlc-spatial-high10", false)]
        [InlineData("avc-mbaff-filter-cavlc-spatial-422-10", false)]
        [InlineData("avc-mbaff-filter-cavlc-spatial-422", false)]
        [InlineData("avc-mbaff-filter-cavlc-temporal-field", false)]
        [InlineData("avc-mbaff-filter-cavlc-temporal-adaptive", false)]
        [InlineData("avc-mbaff-filter-cavlc-temporal-high10", false)]
        [InlineData("avc-mbaff-filter-cavlc-temporal-422-10", false)]
        [InlineData("avc-mbaff-filter-cavlc-temporal-422", false)]
        [InlineData("avc-mbaff-filter-cabac-spatial-field", true)]
        [InlineData("avc-mbaff-filter-cabac-spatial-adaptive", true)]
        [InlineData("avc-mbaff-filter-cabac-spatial-high10", true)]
        [InlineData("avc-mbaff-filter-cabac-spatial-422-10", true)]
        [InlineData("avc-mbaff-filter-cabac-spatial-422", true)]
        [InlineData("avc-mbaff-filter-cabac-temporal-field", true)]
        [InlineData("avc-mbaff-filter-cabac-temporal-adaptive", true)]
        [InlineData("avc-mbaff-filter-cabac-temporal-high10", true)]
        [InlineData("avc-mbaff-filter-cabac-temporal-422-10", true)]
        [InlineData("avc-mbaff-filter-cabac-temporal-422", true)]
        public async Task FilteredMacroblockPairsMatchIndependentDecoder(string name, bool cabac)
        {
            (int frames, int fields) = await H264MbaffSequenceTestInput.VerifyPredictiveAsync(
                Path.GetDirectoryName(Mp4TestData.PathFor(name))!, name, 12, cabac, [0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 11], true);
            Assert.Equal(216, frames + fields);
            Assert.True(fields > 0);
            VideoPixelFormat pixels = name switch
            {
                string value when value.EndsWith("422-10", StringComparison.Ordinal) => VideoPixelFormat.Yuv422P10,
                string value when value.EndsWith("high10", StringComparison.Ordinal) => VideoPixelFormat.Yuv420P10,
                string value when value.EndsWith("422", StringComparison.Ordinal) => VideoPixelFormat.Yuv422P8,
                string value when value.EndsWith("field", StringComparison.Ordinal) || value.EndsWith("adaptive", StringComparison.Ordinal) => VideoPixelFormat.Yuv420P8,
                _ => throw new InvalidDataException("Invalid H.264 test pixel format.")
            };
            await H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), pixels, 96, 96, 12, 12,
                Enumerable.Repeat(VideoScanMode.TopFieldFirst, 12).ToArray());
        }

        [Theory]
        [InlineData("avc-mbaff-slices-cavlc-spatial-adaptive", false)]
        [InlineData("avc-mbaff-slices-cavlc-spatial-422-10", true)]
        [InlineData("avc-mbaff-slices-cavlc-temporal-adaptive", false)]
        [InlineData("avc-mbaff-slices-cavlc-temporal-422-10", true)]
        [InlineData("avc-mbaff-slices-cabac-spatial-adaptive", false)]
        [InlineData("avc-mbaff-slices-cabac-spatial-422-10", true)]
        [InlineData("avc-mbaff-slices-cabac-temporal-adaptive", false)]
        [InlineData("avc-mbaff-slices-cabac-temporal-422-10", true)]
        public async Task MultipleSlicesPreserveFilteredSamplesAndSeekTimestamps(string name, bool highPrecision)
        {
            VideoPixelFormat pixels = highPrecision ? VideoPixelFormat.Yuv422P10 : VideoPixelFormat.Yuv420P8;
            await H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), pixels, 96, 96, 12, 12,
                Enumerable.Repeat(VideoScanMode.TopFieldFirst, 12).ToArray());
        }

        [Theory]
        [InlineData("avc-mbaff-x264-420-tff", "Yuv420P8", VideoScanMode.TopFieldFirst)]
        [InlineData("avc-mbaff-x264-420-bff", "Yuv420P8", VideoScanMode.BottomFieldFirst)]
        [InlineData("avc-mbaff-x264-420-10-tff", "Yuv420P10", VideoScanMode.TopFieldFirst)]
        [InlineData("avc-mbaff-x264-420-10-bff", "Yuv420P10", VideoScanMode.BottomFieldFirst)]
        [InlineData("avc-mbaff-x264-422-tff", "Yuv422P8", VideoScanMode.TopFieldFirst)]
        [InlineData("avc-mbaff-x264-422-bff", "Yuv422P8", VideoScanMode.BottomFieldFirst)]
        [InlineData("avc-mbaff-x264-422-10-tff", "Yuv422P10", VideoScanMode.TopFieldFirst)]
        [InlineData("avc-mbaff-x264-422-10-bff", "Yuv422P10", VideoScanMode.BottomFieldFirst)]
        public async Task CroppedX264SequencesPreserveFieldOrderAndSamples(string name, string format, VideoScanMode scan)
        {
            VideoPixelFormat pixels = format switch
            {
                "Yuv420P8" => VideoPixelFormat.Yuv420P8,
                "Yuv420P10" => VideoPixelFormat.Yuv420P10,
                "Yuv422P8" => VideoPixelFormat.Yuv422P8,
                "Yuv422P10" => VideoPixelFormat.Yuv422P10,
                _ => throw new InvalidDataException("Invalid H.264 test pixel format.")
            };
            await H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), pixels, 94, 92, 12, 12,
                Enumerable.Repeat(scan, 12).ToArray());
        }

        [Fact]
        public void BidirectionalWeightsUseFieldOrderAndFrameWeightIndices() => H264AdaptiveBidirectionalTestInput.Verify();

        [Fact]
        public void ColocatedBlocksFollowFrameFieldAndAdaptiveMappingTable() => H264ColocatedBlockTestInput.Verify();

        [Fact]
        public void AdaptiveReferencesPreserveCoordinatesIdentityWeightsAndStorage() => H264AdaptivePredictionTestInput.Verify();

        [Fact]
        public void CabacPairFlagsPreserveInferredModeSkipContextsAndPayloadOrder() => H264CabacPairTestInput.Verify();

        [Theory]
        [InlineData("avc-mbaff-p-cavlc-field", true, false)]
        [InlineData("avc-mbaff-p-cabac-field", true, true)]
        [InlineData("avc-mbaff-p-cavlc-adaptive", false, false)]
        [InlineData("avc-mbaff-p-cabac-adaptive", false, true)]
        [InlineData("avc-mbaff-p-cavlc-high10", false, false)]
        [InlineData("avc-mbaff-p-cabac-high10", false, true)]
        [InlineData("avc-mbaff-p-cavlc-422-10", true, false)]
        [InlineData("avc-mbaff-p-cabac-422-10", true, true)]
        [InlineData("avc-mbaff-p-cavlc-422", false, false)]
        [InlineData("avc-mbaff-p-cabac-422", false, true)]
        public async Task PredictiveMacroblockPairsMatchIndependentDecoder(string name, bool allFields, bool cabac)
        {
            (int frames, int fields) = await H264MbaffSequenceTestInput.VerifyPredictiveAsync(
                Path.GetDirectoryName(Mp4TestData.PathFor(name))!, name, 6, cabac);
            Assert.Equal(108, frames + fields);
            Assert.True(fields > 0);
            if (allFields)
            {
                Assert.Equal(0, frames);
            }
            else
            {
                Assert.True(frames > 0);
            }
        }
    }
}
