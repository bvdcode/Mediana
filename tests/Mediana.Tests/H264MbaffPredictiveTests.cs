// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264MbaffPredictiveTests
    {
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
