// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Formats and measures completed contiguous segments, including files written by independent requests.
    /// Borrows metadata during each synchronous call and performs no media I/O. The caller supplies actual complete file sizes,
    /// segment durations on one common clock and any independent-decoding declaration.</summary>
    public static class HlsMediaPlaylist
    {
        /// <summary>Creates a VOD playlist. Declaring independent segments requires independent decoding of every contained sample;
        /// this formatter cannot establish that property from segment metadata.</summary>
        public static string Create(IReadOnlyList<HlsMediaSegment> segments, Func<int, string> segmentUri,
            bool independentSegments = false, long? targetDurationSeconds = null, int maximumUriCharacters = 4096)
        {
            Validate(segments);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUriCharacters);
            return HlsVodPlaylist.Create(segments, static segment => segment.Duration, segmentUri,
                maximumUriCharacters, independentSegments, targetDurationSeconds);
        }

        /// <summary>Measures peak and average complete-file bitrates in bits per second using the advertised target duration.
        /// Peak uses contiguous segment sets between half and one-and-a-half of TARGETDURATION, as defined by RFC 8216.
        /// Throws when the presentation has no eligible set; such a peak is undefined.</summary>
        public static (long Peak, long Average) MeasureBandwidth(IReadOnlyList<HlsMediaSegment> segments,
            long? targetDurationSeconds = null)
        {
            Validate(segments);
            return HlsBandwidth.Calculate(segments, targetDurationSeconds);
        }

        private static void Validate(IReadOnlyList<HlsMediaSegment> segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            if (segments.Count == 0)
            {
                throw new ArgumentException("An HLS media playlist requires at least one completed segment.", nameof(segments));
            }
            TimeBase clock = segments[0].Duration.TimeBase;
            clock.Validate();
            MediaTimestamp? previousEnd = null;
            for (int number = 0; number < segments.Count; number++)
            {
                HlsMediaSegment segment = segments[number];
                segment.Start.TimeBase.Validate();
                segment.Duration.TimeBase.Validate();
                if (segment.Number != number || segment.ByteLength <= 0 || segment.Duration.Value <= 0 ||
                    segment.Duration.TimeBase != clock || segment.Start.TimeBase != clock ||
                    (previousEnd is MediaTimestamp end && segment.Start != end))
                {
                    throw new ArgumentException("Completed HLS segments require sequential numbers, positive sizes and durations, and contiguous timestamps on one common clock.", nameof(segments));
                }
                previousEnd = new(checked(segment.Start.Value + segment.Duration.Value), clock);
            }
        }
    }
}
