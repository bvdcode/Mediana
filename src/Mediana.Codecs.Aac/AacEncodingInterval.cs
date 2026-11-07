// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    /// <summary>Plans bounded AAC-LC encoding on a global 1024-sample grid. Source bounds describe the exact PCM timeline.
    /// Adjacent requested intervals own disjoint whole packets, including the initial priming packet only at the source origin.</summary>
    public class AacEncodingInterval
    {
        private const int FrameSamples = 1024;
        private const int WarmupFrames = 4;
        private const int LookaheadFrames = 2;

        private AacEncodingInterval(int sampleRate, long inputFrameOffset, MediaTimeRange input, MediaTimeRange output, bool hasPackets)
        {
            SampleRate = sampleRate;
            InputFrameOffset = inputFrameOffset;
            Input = input;
            Output = output;
            HasPackets = hasPackets;
        }

        public int SampleRate { get; }
        public long InputFrameOffset { get; }
        public MediaTimeRange Input { get; }
        public MediaTimeRange Output { get; }
        public bool HasPackets { get; }

        public static AacEncodingInterval Create(MediaTimeRange requested, MediaTimeRange source, int sampleRate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
            TimeBase clock = TimeBase.Common(source.Start.ReduceClock().TimeBase, source.End.ReduceClock().TimeBase,
                requested.Start.ReduceClock().TimeBase, requested.End.ReduceClock().TimeBase, new(1, sampleRate));
            long origin = source.Start.Rescale(clock, TimestampRounding.TowardZero).Value;
            long sourceEnd = source.End.Rescale(clock, TimestampRounding.TowardZero).Value;
            long sampleTicks = clock.Denominator / sampleRate;
            long sourceTicks = checked(sourceEnd - origin);
            if (sourceTicks % sampleTicks != 0)
            { throw new ArgumentException("AAC source bounds must describe a whole number of PCM samples.", nameof(source)); }
            long samples = sourceTicks / sampleTicks;
            long start = Math.Clamp(requested.Start.Rescale(clock, TimestampRounding.TowardZero).Value, origin, sourceEnd);
            long end = Math.Clamp(requested.End.Rescale(clock, TimestampRounding.TowardZero).Value, origin, sourceEnd);
            long frameTicks = checked(FrameSamples * sampleTicks);
            long first = Ceiling(checked(start - origin), frameTicks);
            long exclusiveEnd = Ceiling(checked(end - origin), frameTicks);
            bool priming = start == origin && end > start;
            bool hasPackets = end > start && (first < exclusiveEnd || priming);
            if (!hasPackets)
            {
                MediaTimeRange empty = new(new(start, clock), new(start, clock));
                return new(sampleRate, 0, empty, empty, false);
            }
            long inputFrame = Math.Max(0, first - WarmupFrames);
            long inputEnd = (long)Int128.Min(samples, ((Int128)exclusiveEnd + LookaheadFrames) * FrameSamples);
            return new(sampleRate, inputFrame,
                new(At(inputFrame * FrameSamples), At(inputEnd)),
                new(At(priming ? -FrameSamples : checked(first * FrameSamples)), At(checked(exclusiveEnd * FrameSamples))), true);

            MediaTimestamp At(long sample) => new(checked(origin + checked(sample * sampleTicks)), clock);
        }

        private static long Ceiling(long value, long divisor) => value / divisor + (value % divisor == 0 ? 0 : 1);
    }
}
