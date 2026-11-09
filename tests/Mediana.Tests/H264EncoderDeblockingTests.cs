// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class H264EncoderDeblockingTests
    {
        [Theory]
        [InlineData(H264EntropyCoding.Cavlc, H264MacroblockAnalysis.Fast, 1, 26, 0, 0)]
        [InlineData(H264EntropyCoding.Cabac, H264MacroblockAnalysis.Fast, 1, 26, 0, 0)]
        [InlineData(H264EntropyCoding.Cabac, H264MacroblockAnalysis.RateDistortion, 1, 26, 0, 0)]
        [InlineData(H264EntropyCoding.Cabac, H264MacroblockAnalysis.RateDistortion, 3, 38, -6, 6)]
        [InlineData(H264EntropyCoding.Cavlc, H264MacroblockAnalysis.Fast, 3, 38, 6, -6)]
        [InlineData(H264EntropyCoding.Cabac, H264MacroblockAnalysis.RateDistortion, 2, 18, 2, -2)]
        [InlineData(H264EntropyCoding.Cabac, H264MacroblockAnalysis.Fast, 0, 0, -6, -6)]
        [InlineData(H264EntropyCoding.Cavlc, H264MacroblockAnalysis.Fast, 1, 51, 6, 6)]
        public async Task CompleteFilteredReferencesMatchIndependentPixels(H264EntropyCoding entropy,
            H264MacroblockAnalysis analysis, int mode, int qp, int alpha, int beta)
        {
            H264DeblockingOptions options = new() { AlphaOffset = alpha, BetaOffset = beta };
            await H264TemporalEncodingTestInput.VerifyDeblockingAsync(
                Path.Combine(AppContext.BaseDirectory, "Data", "H264DeblockingEncoding"), mode, qp, entropy, analysis, options);
        }

        [Theory]
        [InlineData(-7, 0)]
        [InlineData(7, 0)]
        [InlineData(0, -7)]
        [InlineData(0, 7)]
        public void InvalidThresholdOffsetsFailBeforeAllocating(int alpha, int beta)
        {
            MediaBufferAllocator allocator = new(65536);
            H264EncoderOptions options = new() { Deblocking = new() { AlphaOffset = alpha, BetaOffset = beta } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                H264Encoder.Create(H264EncoderTests.Format(32, 32), new(1, 1000), allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
