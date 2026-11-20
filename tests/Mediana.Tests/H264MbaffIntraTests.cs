// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264MbaffIntraTests
    {
        [Theory]
        [InlineData("avc-mbaff-intra-cavlc", false)]
        [InlineData("avc-mbaff-intra-field", true)]
        [InlineData("avc-mbaff-intra-high10", false)]
        [InlineData("avc-mbaff-intra-422-10", false)]
        public async Task IntraPredictionMatchesIndependentDecodedPixels(string name, bool requireFields)
        {
            int fieldPairs = await H264FieldTestInput.VerifyMbaffIntraAsync(
                Path.GetDirectoryName(Mp4TestData.PathFor(name + ".mp4"))!, name, requireFields);
            if (requireFields)
            {
                Assert.Equal(18, fieldPairs);
            }
            else
            {
                Assert.InRange(fieldPairs, 1, 17);
            }
        }
    }
}
