// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

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
