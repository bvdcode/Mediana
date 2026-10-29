// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Flac;
using Mediana.Core.Audio;

namespace Mediana.Tests
{
    public class FlacFrameHeaderTests
    {
        [Theory]
        [InlineData(0L, false)]
        [InlineData(127L, false)]
        [InlineData(128L, false)]
        [InlineData(0x800L, false)]
        [InlineData(0x10000L, false)]
        [InlineData(0x200000L, false)]
        [InlineData(0x4000000L, false)]
        [InlineData(0x7fffffffL, false)]
        [InlineData(0x80000000L, true)]
        [InlineData(0xfffffffffL, true)]
        public void ExtendedNumbersAndPartialHeadersFollowTheFlacNumberRanges(long number, bool variable)
        {
            byte[] data = new FlacFrameTestWriter(257, 1, number, variable).Prefix();
            FlacConfiguration configuration = FlacConfiguration.ReadStreamInfo(FlacTestInput.StreamInfo());
            for (int length = 0; length < data.Length; length++)
            {
                Assert.False(FlacFrameHeader.TryRead(data.AsSpan(0, length), configuration, out _));
            }
            Assert.True(FlacFrameHeader.TryRead(data, configuration, out FlacFrameHeader header));
            Assert.Equal(number, header.Number);
            Assert.Equal(variable, header.VariableBlockSize);
            Assert.Equal(257, header.BlockSize);
            data[^1] ^= 1;
            Assert.Throws<InvalidDataException>(() => FlacFrameHeader.TryRead(data, configuration, out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        public void InheritedIndexedAndExtendedSampleRatesAgreeWithStreamInfo(int code)
        {
            byte[] data = new FlacFrameTestWriter(16, 1, rateCode: code).Prefix();
            FlacConfiguration configuration = FlacConfiguration.ReadStreamInfo(FlacTestInput.StreamInfo());
            Assert.True(FlacFrameHeader.TryRead(data, configuration, out _));
        }
    }
}
