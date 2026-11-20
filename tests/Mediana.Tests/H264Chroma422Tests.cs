// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264Chroma422Tests
    {
        [Theory]
        [InlineData("avc-high422-cabac")]
        [InlineData("avc-high422-cavlc")]
        [InlineData("avc-high422-cabac-q1")]
        [InlineData("avc-high422-cavlc-q40")]
        public Task AllFramesAndReverseSeeksMatchIndependentDecoder(string name) =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), VideoPixelFormat.Yuv422P8, 82, 74, 24, 12);
    }
}
