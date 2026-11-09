// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsBandwidthPolicyTests
    {
        [Fact]
        public void ExplicitDefaultStillRejectsAnUndefinedRfcPeak()
        {
            Assert.Throws<NotSupportedException>(() => HlsMediaPlaylist.MeasureBandwidth(Segments(), 1, new()));
        }

        [Fact]
        public void InvalidModeAndInvalidMetadataCannotBecomeEstimates()
        {
            Assert.Throws<ArgumentNullException>(() => HlsMediaPlaylist.MeasureBandwidth(Segments(), 1, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => HlsMediaPlaylist.MeasureBandwidth(Segments(), 1,
                new() { PeakMode = (HlsPeakBandwidthMode)int.MaxValue }));
            HlsMediaSegment[] segments = Segments();
            segments[0] = segments[0] with { ByteLength = 0 };
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.MeasureBandwidth(segments, 1,
                new() { PeakMode = HlsPeakBandwidthMode.EstimateShortPresentation }));
        }

        private static HlsMediaSegment[] Segments() => [new(0, new(0, new(1, 1000)), new(100, new(1, 1000)), 188)];
    }
}
