using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Immutable selected video and audio ranges. Duration uses the TS clock and includes a declared audio tail in the final segment.</summary>
    public class Mp4HlsSegment
    {
        internal Mp4HlsSegment(Mp4AvcSegment video, Mp4HlsAudioRange? audio, bool final)
        {
            Video = video;
            Audio = audio;
            TimeBase clock = new(1, 90000);
            long start = video.PresentationStart.Rescale(clock, TimestampRounding.NearestEven).Value;
            long end = new MediaTimestamp(checked(video.PresentationStart.Value + video.Duration.Value), video.Duration.TimeBase)
                .Rescale(clock, TimestampRounding.NearestEven).Value;
            if (final && audio?.PresentationEnd is MediaTimestamp audioEnd)
            {
                end = Math.Max(end, audioEnd.Rescale(clock, TimestampRounding.NearestEven).Value);
            }
            Duration = new(checked(end - start), clock);
            if (Duration.Value == 0)
            {
                throw new NotSupportedException("The segment duration is below the TS clock resolution.");
            }
        }

        public int Number => Video.Number;
        public Mp4AvcSegment Video { get; }
        public Mp4HlsAudioRange? Audio { get; }
        public MediaDuration Duration { get; }
    }
}
