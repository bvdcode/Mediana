// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    internal class AudioSampleClock
    {
        private readonly MediaTimestamp? origin;
        private readonly TimeBase timeBase;
        private readonly long originTicks;
        private readonly int inputStep;
        private readonly int outputStep;
        private readonly int inputRate;

        public AudioSampleClock(MediaTimestamp? origin, int inputRate, int outputRate)
        {
            this.origin = origin;
            this.inputRate = inputRate;
            if (origin is MediaTimestamp timestamp)
            {
                int denominator = LeastCommonMultiple(LeastCommonMultiple(inputRate, outputRate), timestamp.TimeBase.Denominator);
                timeBase = new TimeBase(1, denominator);
                originTicks = timestamp.Rescale(timeBase, TimestampRounding.TowardZero).Value;
                inputStep = denominator / inputRate;
                outputStep = denominator / outputRate;
            }
        }

        public MediaTimestamp? AtOutput(long position)
        {
            if (origin is null)
            {
                return null;
            }
            return new MediaTimestamp(checked(originTicks + position * outputStep), timeBase);
        }

        public long PositionAtOrAfter(MediaTimestamp timestamp)
        {
            timestamp.TimeBase.Validate();
            MediaTimestamp first = origin
                ?? throw new NotSupportedException("Time-based audio selection requires known PCM timestamps.");
            Int128 difference = (Int128)timestamp.Value * timestamp.TimeBase.Numerator * first.TimeBase.Denominator
                - (Int128)first.Value * first.TimeBase.Numerator * timestamp.TimeBase.Denominator;
            if (difference <= 0)
            {
                return 0;
            }
            Int128 denominator = (Int128)timestamp.TimeBase.Denominator * first.TimeBase.Denominator;
            long whole = checked((long)(difference / denominator) * inputRate);
            Int128 fraction = difference % denominator * inputRate;
            long samples = checked(whole + (long)(fraction / denominator));
            return fraction % denominator == 0 ? samples : checked(samples + 1);
        }

        public void Validate(MediaTimestamp? timestamp, long position)
        {
            if (origin is null)
            {
                if (timestamp is not null)
                {
                    throw new NotSupportedException("PCM timestamps changed from unknown to known.");
                }
                return;
            }
            MediaTimestamp expected = new(checked(originTicks + position * inputStep), timeBase);
            if (timestamp is not MediaTimestamp actual)
            {
                throw new InvalidDataException($"The PCM timestamp is missing at input sample {position}.");
            }
            if (actual != expected)
            {
                throw new InvalidDataException($"PCM timestamp discontinuity at input sample {position}: " +
                    $"expected {expected.Value} ticks at {expected.TimeBase.Numerator}/{expected.TimeBase.Denominator} seconds, " +
                    $"received {actual.Value} ticks at {actual.TimeBase.Numerator}/{actual.TimeBase.Denominator} seconds.");
            }
        }

        internal static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return a;
        }

        private static int LeastCommonMultiple(int a, int b)
        {
            long result = (long)(a / GreatestCommonDivisor(a, b)) * b;
            if (result > int.MaxValue)
            {
                throw new NotSupportedException("The audio rates and timestamp cannot share an exact supported time base.");
            }
            return (int)result;
        }
    }
}
