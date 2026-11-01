// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class AacAutomaticEncodingTests
    {
        [Theory]
        [InlineData(96000)]
        [InlineData(88200)]
        [InlineData(64000)]
        [InlineData(48000)]
        [InlineData(44100)]
        [InlineData(32000)]
        [InlineData(24000)]
        [InlineData(22050)]
        [InlineData(16000)]
        [InlineData(12000)]
        [InlineData(11025)]
        [InlineData(8000)]
        public void EveryRatePreservesCompleteInputAndBlockIndependence(int rate)
        {
            foreach (int channels in new[] { 1, 2 })
            {
                foreach (int samples in new[] { 1, 1023, 1024, 1025, 2048, 2051, 4133 })
                {
                    AacAutomaticEncodingTestInput.Verify(rate, channels, samples);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(447)]
        [InlineData(448)]
        [InlineData(511)]
        [InlineData(575)]
        [InlineData(576)]
        [InlineData(1023)]
        public void AttacksInEitherChannelProtectBothOverlappingWindows(int offset)
        {
            AacAutomaticEncodingTestInput.Verify(48000, 2, 4133, offset, 0);
            AacAutomaticEncodingTestInput.Verify(48000, 2, 4133, offset, 1);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void MinimumBudgetAccountsForEightIndependentShortGroups(int channels)
        {
            const int Rate = 48000;
            int bits = (AacPacketEncoder.MinimumFrameBits(3, channels) + 7) & ~7;
            int bitRate = (bits * Rate + 1023) / 1024;
            MediaBufferAllocator allocator = new(1024 * 1024);
            Assert.Throws<ArgumentOutOfRangeException>(() => AacEncoder.Create(AacEncoderTests.Format(Rate, channels), new(1, Rate),
                allocator, new() { BitRate = bitRate - 1 }));
            Assert.Equal(0, allocator.AllocatedBytes);
            AacAutomaticEncodingTestInput.Verify(Rate, channels, 4133, bitRate: bitRate);
        }
    }
}
