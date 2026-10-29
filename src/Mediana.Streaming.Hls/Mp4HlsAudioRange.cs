using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Zero-based AAC sample coverage in a video segment's decode window.
    /// Presentation bounds follow MP4 sample durations. Empty ranges have null bounds; priming and padding are retained.</summary>
    public readonly record struct Mp4HlsAudioRange(int StreamIndex, int FirstSample, int SampleCount,
        MediaTimestamp? PresentationStart, MediaTimestamp? PresentationEnd);
}
