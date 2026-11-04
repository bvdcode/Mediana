// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsCompletedSegmentsTests
    {
        [Fact]
        public void OriginalAndRegeneratedFileSizesUseTheSameExactPlaylistAndRateRules() => HlsCompletedSegmentsTestInput.Verify();

        [Theory]
        [InlineData("number")]
        [InlineData("empty")]
        [InlineData("negative-size")]
        [InlineData("zero-duration")]
        [InlineData("duration-clock")]
        [InlineData("start-clock")]
        [InlineData("gap")]
        [InlineData("overlap")]
        public void InvalidCompletedMetadataFailsBeforeAnyUriIsRequested(string scenario)
        {
            HlsMediaSegment[] segments = Segments();
            segments[1] = scenario switch
            {
                "number" => segments[1] with { Number = 0 },
                "empty" => segments[1] with { ByteLength = 0 },
                "negative-size" => segments[1] with { ByteLength = -1 },
                "zero-duration" => segments[1] with { Duration = new(0, new(1, 1000)) },
                "duration-clock" => segments[1] with { Duration = new(1, new(1, 1)) },
                "start-clock" => segments[1] with { Start = new(1, new(1, 1)) },
                "gap" => segments[1] with { Start = new(1001, new(1, 1000)) },
                "overlap" => segments[1] with { Start = new(999, new(1, 1000)) },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            bool uriRequested = false;
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.Create(segments, _ =>
            {
                uriRequested = true;
                return "segment.ts";
            }));
            Assert.False(uriRequested);
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.MeasureBandwidth(segments));
        }

        [Fact]
        public void MissingSegmentsInvalidTargetsAndUriInjectionAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => HlsMediaPlaylist.Create(null!, _ => "segment.ts"));
            Assert.Throws<ArgumentNullException>(() => HlsMediaPlaylist.MeasureBandwidth(null!));
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.Create([], _ => "segment.ts"));
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.MeasureBandwidth([]));
            Assert.Throws<ArgumentNullException>(() => HlsMediaPlaylist.Create(Segments(), null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => HlsMediaPlaylist.Create(Segments(), _ => "s.ts", maximumUriCharacters: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HlsMediaPlaylist.MeasureBandwidth(Segments(), 0));
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.Create(Segments(), _ => "s.ts\n#EXT-X-ENDLIST"));
            Assert.Throws<ArgumentException>(() => HlsMediaPlaylist.Create(Segments(), _ => "long.ts", maximumUriCharacters: 4));
        }

        [Fact]
        public void ShortPresentationCanBeFormattedWithoutInventingPeakBandwidth()
        {
            HlsMediaSegment[] segments = [Segments()[0]];
            Assert.Contains("#EXT-X-TARGETDURATION:6\n", HlsMediaPlaylist.Create(segments, _ => "s.ts", targetDurationSeconds: 6));
            Assert.Throws<NotSupportedException>(() => HlsMediaPlaylist.MeasureBandwidth(segments, 6));
        }

        [Fact]
        public void UnrepresentableEndAndTotalSizeFailWithCheckedArithmetic()
        {
            HlsMediaSegment[] segments = Segments();
            segments[0] = segments[0] with { Start = new(long.MaxValue, new(1, 1000)) };
            Assert.Throws<OverflowException>(() => HlsMediaPlaylist.MeasureBandwidth(segments));
            segments = Segments();
            segments[0] = segments[0] with { ByteLength = long.MaxValue };
            Assert.Throws<OverflowException>(() => HlsMediaPlaylist.MeasureBandwidth(segments));
        }

        private static HlsMediaSegment[] Segments() =>
        [
            new(0, new(0, new(1, 1000)), new(1000, new(1, 1000)), 1880),
            new(1, new(1000, new(1, 1000)), new(1000, new(1, 1000)), 3760)
        ];
    }
}
