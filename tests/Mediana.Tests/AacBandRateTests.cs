// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;

namespace Mediana.Tests
{
    public class AacBandRateTests
    {
        [Theory]
        [InlineData(3, 14)]
        [InlineData(5, 48)]
        public void BandReplacementMatchesCountingEverySectionAndScaleDifference(int lengthBits, int groupSize)
        {
            Random random = new(111);
            for (int trial = 0; trial < 48; trial++)
            {
                AacBandQuantization[] bands = new AacBandQuantization[2 * groupSize];
                for (int i = 0; i < bands.Length; i++)
                {
                    byte book = (byte)(trial < 3 ? trial : random.Next(5));
                    bands[i] = new(random.Next(80, 130), book, book == 0 ? 0 : random.Next(50), 0);
                }
                int original = AacChannelEncoder.Count(bands, groupSize, lengthBits);
                for (int index = 0; index < bands.Length; index++)
                {
                    AacBandQuantization saved = bands[index];
                    for (byte book = 0; book <= 4; book++)
                    {
                        AacBandQuantization candidate = new(random.Next(256), book, book == 0 ? 0 : random.Next(50), 0);
                        int estimate = AacBandRate.CountReplacement(bands, index, candidate, original, groupSize, lengthBits);
                        bands[index] = candidate;
                        Assert.Equal(AacChannelEncoder.Count(bands, groupSize, lengthBits), estimate);
                        bands[index] = saved;
                    }
                }
            }
        }
    }
}
