// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264MbaffPredictiveTests
    {
        [Fact]
        public void AdaptiveReferencesPreserveCoordinatesIdentityWeightsAndStorage() => H264AdaptivePredictionTestInput.Verify();

        [Theory]
        [InlineData("avc-mbaff-p-cavlc-field", true)]
        [InlineData("avc-mbaff-p-cavlc-adaptive", false)]
        [InlineData("avc-mbaff-p-cavlc-high10", false)]
        [InlineData("avc-mbaff-p-cavlc-422-10", true)]
        [InlineData("avc-mbaff-p-cavlc-422", false)]
        public async Task PredictiveMacroblockPairsMatchIndependentDecoder(string name, bool allFields)
        {
            (int frames, int fields) = await H264MbaffSequenceTestInput.VerifyPredictiveAsync(
                Path.GetDirectoryName(Mp4TestData.PathFor(name))!, name, 6);
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
