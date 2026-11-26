// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;

namespace Mediana.Tests
{
    public class H264SampleDifferenceTests
    {
        [Theory]
        [InlineData(0, 255)]
        [InlineData(255, 0)]
        [InlineData(17, 201)]
        public void PackedMacroblockDifferenceRetainsTheFullSum(byte sourceValue, byte predictionValue)
        {
            byte[] source = new byte[H264SourceMacroblock.ByteCount];
            byte[] prediction = new byte[H264SourceMacroblock.ByteCount];
            source.AsSpan().Fill(sourceValue);
            prediction.AsSpan().Fill(predictionValue);

            int expected = source.Length * Math.Abs(sourceValue - predictionValue);
            Assert.Equal(expected, H264SampleDifference.SumPackedMacroblock(source, prediction));
        }
    }
}
