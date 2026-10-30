// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal static class HlsBandwidth
    {
        public static (long Peak, long Average) Calculate(IReadOnlyList<HlsMediaSegment> segments)
        {
            TimeBase clock = segments[0].Duration.TimeBase;
            long targetSeconds = segments.Max(segment => new MediaTimestamp(segment.Duration.Value, segment.Duration.TimeBase)
                .Rescale(new(1, 1), TimestampRounding.Ceiling).Value);
            long totalBytes = 0;
            Int128 totalDuration = 0;
            long peak = 0;
            for (int first = 0; first < segments.Count; first++)
            {
                totalBytes = checked(totalBytes + segments[first].ByteLength);
                totalDuration += segments[first].Duration.Value;
                long bytes = 0;
                Int128 duration = 0;
                for (int last = first; last < segments.Count; last++)
                {
                    duration += segments[last].Duration.Value;
                    Int128 twiceSeconds = duration * clock.Numerator * 2;
                    if (twiceSeconds > (Int128)targetSeconds * clock.Denominator * 3)
                    {
                        break;
                    }
                    bytes = checked(bytes + segments[last].ByteLength);
                    if (twiceSeconds >= (Int128)targetSeconds * clock.Denominator)
                    {
                        peak = Math.Max(peak, Rate(bytes, duration, clock));
                    }
                }
            }
            if (peak == 0)
            {
                throw new NotSupportedException("The presentation has no contiguous segment set between half and one-and-a-half of TARGETDURATION; RFC 8216 peak bandwidth is undefined.");
            }
            return (peak, Rate(totalBytes, totalDuration, clock));
        }

        private static long Rate(long bytes, Int128 duration, TimeBase clock)
        {
            Int128 numerator = (Int128)bytes * 8 * clock.Denominator;
            Int128 denominator = duration * clock.Numerator;
            return checked((long)((numerator + denominator - 1) / denominator));
        }
    }
}
