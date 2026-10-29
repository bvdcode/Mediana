using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>One video window with zero-based decode-order samples and an exclusive decode end.
    /// Duration covers the video presentation interval; audio and transport state are not included.</summary>
    public readonly record struct Mp4AvcSegment(int Number, int FirstSample, int SampleCount,
        MediaTimestamp PresentationStart, MediaDuration Duration, MediaTimestamp DecodeStart, MediaTimestamp DecodeEnd);
}
