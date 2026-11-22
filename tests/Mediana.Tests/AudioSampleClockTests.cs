// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioSampleClockTests
    {
        [Theory]
        [InlineData(48000, 0)]
        [InlineData(48000, 123000000)]
        [InlineData(48000, -123000000)]
        [InlineData(44100, 0)]
        [InlineData(44100, 123000000)]
        [InlineData(44100, -123000000)]
        public void NanosecondOriginsKeepExactInputAndOutputSamplePositions(int rate, long nanoseconds)
        {
            MediaTimestamp origin = new(nanoseconds, new(1, 1000000000));
            AudioSampleClock clock = new(origin, rate, 400);
            TimeBase exact = TimeBase.Common(new(1, 1000), new(1, rate), new(1, 400));
            long start = origin.Rescale(exact, TimestampRounding.TowardZero).Value;
            foreach (long samples in new long[] { 0, 1, 512, 123456789 })
            {
                MediaTimestamp input = new(start + samples * (exact.Denominator / rate), exact);
                clock.Validate(input, samples);
                Assert.Equal(samples, clock.PositionAtOrAfter(input));
                Assert.Equal(new MediaTimestamp(start + samples * (exact.Denominator / 400), exact), clock.AtOutput(samples));
                Assert.Throws<InvalidDataException>(() => clock.Validate(new(input.Value + exact.Denominator / rate, exact), samples));
            }
        }
    }
}
