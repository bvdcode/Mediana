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
                        int delta = AacBandRate.ReplacementDelta(bands, index, candidate, groupSize, lengthBits);
                        int estimate = delta == int.MaxValue ? int.MaxValue : original + delta;
                        bands[index] = candidate;
                        Assert.Equal(AacChannelEncoder.Count(bands, groupSize, lengthBits), estimate);
                        bands[index] = saved;
                    }
                }
            }
        }

        [Theory]
        [InlineData(3, 14)]
        [InlineData(5, 48)]
        public void RatesOutsideChangedRangeSurviveCodebookAndScaleChanges(int lengthBits, int groupSize)
        {
            Random random = new(662);
            for (int trial = 0; trial < 64; trial++)
            {
                AacBandQuantization[] bands = new AacBandQuantization[3 * groupSize];
                for (int i = 0; i < bands.Length; i++)
                {
                    byte book = (byte)(trial < 24 ? i / 8 % 4 : random.Next(5));
                    bands[i] = new(random.Next(80, 130), book, book == 0 ? 0 : random.Next(50), 0);
                }
                AacBandQuantization[] original = (AacBandQuantization[])bands.Clone();
                int changed = random.Next(bands.Length);
                byte nextBook = (byte)random.Next(5);
                bands[changed] = new(random.Next(80, 130), nextBook, nextBook == 0 ? 0 : random.Next(50), 0);
                (int start, int length) = AacBandRate.ChangedRange(bands, changed, original[changed], groupSize);
                Assert.InRange(changed, start, start + length - 1);
                for (int index = 0; index < bands.Length; index++)
                {
                    if (index >= start && index < start + length)
                    {
                        continue;
                    }
                    for (byte book = 0; book <= 4; book++)
                    {
                        AacBandQuantization candidate = new(random.Next(256), book, book == 0 ? 0 : random.Next(50), 0);
                        int cached = AacBandRate.ReplacementDelta(original, index, candidate, groupSize, lengthBits);
                        Assert.Equal(cached, AacBandRate.ReplacementDelta(bands, index, candidate, groupSize, lengthBits));
                    }
                }
            }
        }
    }
}
