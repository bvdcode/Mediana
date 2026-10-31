// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs.H264
{
    internal class H264RateController(H264RateControlOptions options, TimeBase timeBase, int initialQuantizer)
    {
        private readonly H264BitCredit credit = new(options.BitRate, options.BufferBits, timeBase);
        private readonly int initial = initialQuantizer;
        private long intraCost;
        private long interCost;
        private int intraQuantizer = initialQuantizer;
        private int interQuantizer = initialQuantizer;

        public H264FrameRateBudget Prepare(MediaTimestamp timestamp, long duration, bool keyFrame)
        {
            Int128 available = credit.Available(timestamp.Value);
            Int128 capacity = credit.Capacity;
            int maximumBytes = (int)(available / timeBase.Denominator / 8);
            Int128 nominal = Int128.Min(capacity, (Int128)options.BitRate * duration * timeBase.Numerator);
            Int128 spending = nominal + (available - capacity / 2) * nominal / capacity;
            if (keyFrame)
            {
                spending *= 2;
            }
            spending = Int128.Clamp(spending, timeBase.Denominator * 8L, Int128.Max(timeBase.Denominator * 8L, available));
            long cost = keyFrame ? intraCost : interCost;
            int last = keyFrame ? intraQuantizer : interQuantizer;
            int quantizer = initial;
            if (cost != 0)
            {
                quantizer = 51;
                for (int candidate = 0; candidate < 51; candidate++)
                {
                    if ((Int128)cost * timeBase.Denominator <= spending * Scale(candidate))
                    {
                        quantizer = candidate;
                        break;
                    }
                }
                quantizer = Math.Clamp(quantizer, Math.Max(0, last - 6), Math.Min(51, last + 6));
            }
            return new(maximumBytes, quantizer);
        }

        public void Commit(MediaTimestamp timestamp, int quantizer, int bytes, bool keyFrame)
        {
            credit.Commit(timestamp.Value, bytes);
            long observed = (long)bytes * 8 * Scale(quantizer);
            if (keyFrame)
            {
                intraCost = intraCost == 0 ? observed : (intraCost * 3 + observed + 2) / 4;
                intraQuantizer = quantizer;
            }
            else
            {
                interCost = interCost == 0 ? observed : (interCost * 3 + observed + 2) / 4;
                interQuantizer = quantizer;
            }
        }

        public void Reset()
        {
            credit.Reset();
            intraCost = 0;
            interCost = 0;
            intraQuantizer = initial;
            interQuantizer = initial;
        }

        public static int NextQuantizer(int quantizer, int bytes, int maximumBytes)
        {
            long cost = (long)bytes * Scale(quantizer);
            for (int candidate = quantizer + 1; candidate < 51; candidate++)
            {
                if (cost <= (long)maximumBytes * Scale(candidate))
                {
                    return candidate;
                }
            }
            return 51;
        }

        private static int Scale(int quantizer)
        {
            ReadOnlySpan<int> steps = [10, 11, 13, 14, 16, 18];
            return steps[quantizer % 6] << (quantizer / 6);
        }
    }
}
