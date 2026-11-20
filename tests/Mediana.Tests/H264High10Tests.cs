// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264High10Tests
    {
        [Theory]
        [InlineData("avc-high10-cabac")]
        [InlineData("avc-high10-cavlc")]
        [InlineData("avc-high10-cabac-q1")]
        [InlineData("avc-high10-cavlc-q1")]
        public Task High10FramesAndReverseSeeksMatchIndependentDecoder(string name) =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), VideoPixelFormat.Yuv420P10, 82, 74, 24, 12);

        [Theory]
        [InlineData("avc-high422-10-cabac")]
        [InlineData("avc-high422-10-cavlc")]
        [InlineData("avc-high422-10-cabac-q45")]
        [InlineData("avc-high422-10-cavlc-q1")]
        public Task High422TenBitFramesAndReverseSeeksMatchIndependentDecoder(string name) =>
            H264ProfileSequenceTestInput.VerifyAsync(Mp4TestData.PathFor(name), VideoPixelFormat.Yuv422P10, 82, 74, 24, 12);

        [Theory]
        [InlineData(60, 10240, 5120, 163840, 327680, 229376)]
        [InlineData(63, 14336, 7168, 229376, 458752, 327680)]
        public void HighQuantizerTransformsUseTheFullPrimeRange(int qp, int block4, int block8, int lumaDc, int chroma420Dc, int chroma422Dc)
        {
            int[] coefficients = new int[64];
            coefficients[0] = 64;
            int[] output = new int[64];
            H264InverseTransform.Residual4x4(coefficients.AsSpan(0, 16), qp, output.AsSpan(0, 16), bitDepth: 10);
            Assert.All(output[..16], value => Assert.Equal(block4, value));
            H264InverseTransform8x8.Residual(coefficients, qp, output, 10);
            Assert.All(output, value => Assert.Equal(block8, value));
            H264InverseTransform.LumaDc(coefficients.AsSpan(0, 16), qp, output.AsSpan(0, 16), 10);
            Assert.All(output[..16], value => Assert.Equal(lumaDc, value));
            H264InverseTransform.ChromaDc(coefficients.AsSpan(0, 4), qp, output.AsSpan(0, 4), 10);
            Assert.All(output[..4], value => Assert.Equal(chroma420Dc, value));
            H264InverseTransform.ChromaDc(coefficients.AsSpan(0, 8), qp, output.AsSpan(0, 8), 10);
            Assert.All(output[..8], value => Assert.Equal(chroma422Dc, value));
        }
    }
}
