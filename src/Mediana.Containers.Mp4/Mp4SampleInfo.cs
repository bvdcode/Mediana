using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    /// <summary>One encoded sample in decode order. Indices are zero-based, offset is absolute,
    /// and times use the reader's selected timeline. IsSync is a container declaration.</summary>
    public readonly record struct Mp4SampleInfo(int StreamIndex, int SampleIndex, long Offset, int Size,
        MediaTimestamp PresentationTime, MediaTimestamp DecodeTime, MediaDuration Duration, bool IsSync);
}
