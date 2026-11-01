// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs.H264
{
    internal class H264BitCredit(int bitRate, int capacityBits, TimeBase timeBase)
    {
        private Int128 credit = (Int128)capacityBits * timeBase.Denominator;
        private long? previous;

        public Int128 Capacity { get; } = (Int128)capacityBits * timeBase.Denominator;

        /// <summary>Available bits multiplied by the clock denominator, retaining fractional refill.</summary>
        public Int128 Available(long timestamp)
        {
            if (previous is not long last)
            {
                return Capacity;
            }
            Int128 elapsed = (Int128)timestamp - last;
            if (elapsed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp), "A bit budget cannot move backwards in time.");
            }
            return Int128.Min(Capacity, credit + elapsed * timeBase.Numerator * bitRate);
        }

        public void Commit(long timestamp, int bytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            Int128 remaining = Available(timestamp) - (Int128)bytes * 8 * timeBase.Denominator;
            if (remaining < 0)
            {
                throw new InvalidOperationException("The encoded bytes exceed the available bit budget.");
            }
            credit = remaining;
            previous = timestamp;
        }

        public void Reset()
        {
            credit = Capacity;
            previous = null;
        }
    }
}
