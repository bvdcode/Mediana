using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;

namespace Mediana.Streaming.Hls
{
    public record Mp4HlsOptions
    {
        public Mp4ReadOptions Reading { get; init; } = new() { TimestampMode = Mp4TimestampMode.LinearMovie };
        public HlsPlanningOptions Planning { get; init; } = new();
        public MpegTsWriteOptions Transport { get; init; } = new();
        public int MaximumSegmentUriCharacters { get; init; } = 4096;

        internal void Validate()
        {
            ArgumentNullException.ThrowIfNull(Reading);
            ArgumentNullException.ThrowIfNull(Planning);
            ArgumentNullException.ThrowIfNull(Transport);
            Planning.Validate();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumSegmentUriCharacters);
            if (Reading.TimestampMode != Mp4TimestampMode.LinearMovie)
            {
                throw new ArgumentException("An MP4 HLS session requires the linear movie timestamp mode.", nameof(Reading));
            }
        }
    }
}
