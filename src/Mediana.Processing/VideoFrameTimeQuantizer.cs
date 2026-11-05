// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Quantizes presentation boundaries to a destination clock using nearest-even rounding.
    /// Duration is the difference of quantized end and start, so fractional frame periods do not accumulate rounding drift.</summary>
    public class VideoFrameTimeQuantizer
    {
        private readonly TimeBase target;

        public VideoFrameTimeQuantizer(TimeBase target)
        {
            target.Validate();
            this.target = target;
        }

        public VideoFrame Apply(VideoFrame input)
        {
            ArgumentNullException.ThrowIfNull(input);
            MediaTimestamp timestamp = input.Timestamp ?? throw new NotSupportedException("Frame time quantization requires a presentation timestamp.");
            MediaDuration duration = input.Duration ?? throw new NotSupportedException("Frame time quantization requires a declared duration.");
            long start = timestamp.Rescale(target, TimestampRounding.NearestEven).Value;
            Int128 numerator = checked(((Int128)timestamp.Value * timestamp.TimeBase.Numerator * duration.TimeBase.Denominator
                + (Int128)duration.Value * duration.TimeBase.Numerator * timestamp.TimeBase.Denominator) * target.Denominator);
            Int128 denominator = checked((Int128)timestamp.TimeBase.Denominator * duration.TimeBase.Denominator * target.Numerator);
            long end = Round(numerator, denominator);
            return input.WithTiming(new(start, target), new(checked(end - start), target));
        }

        private static long Round(Int128 numerator, Int128 denominator)
        {
            Int128 quotient = numerator / denominator;
            Int128 remainder = numerator % denominator;
            Int128 twice = Int128.Abs(remainder) * 2;
            if (twice > denominator || (twice == denominator && quotient % 2 != 0)) { quotient += numerator.CompareTo(0); }
            return checked((long)quotient);
        }
    }
}
