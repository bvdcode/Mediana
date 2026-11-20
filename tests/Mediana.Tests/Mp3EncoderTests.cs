// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    public class Mp3EncoderTests
    {
        [Theory]
        [InlineData(48000, 2, 192000)]
        [InlineData(44100, 1, 128000)]
        [InlineData(32000, 2, 128000)]
        [InlineData(24000, 2, 96000)]
        [InlineData(22050, 1, 64000)]
        [InlineData(16000, 2, 64000)]
        [InlineData(12000, 1, 32000)]
        [InlineData(11025, 2, 64000)]
        [InlineData(8000, 1, 24000)]
        public void EncodesAllVersionsWithExactPcmAccounting(int rate, int channels, int bitRate)
        {
            Mp3EncodeTestInput.Verify(rate, channels, bitRate);
        }
    }
}
