// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class H264MbaffPcmTests
    {
        [Theory]
        [InlineData("avc-mbaff-pcm-field", true)]
        [InlineData("avc-mbaff-pcm-adaptive", false)]
        [InlineData("avc-mbaff-cabac-pcm-field", true)]
        [InlineData("avc-mbaff-cabac-pcm-adaptive", false)]
        public Task MacroblockPairsMatchIndependentDecodedPixels(string name, bool requireFields) =>
            H264FieldTestInput.VerifyMbaffIntraAsync(Path.GetDirectoryName(Mp4TestData.PathFor(name + ".mp4"))!, name, requireFields);
    }
}
