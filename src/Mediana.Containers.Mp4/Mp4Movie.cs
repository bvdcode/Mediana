using Mediana.Core.Timing;

namespace Mediana.Containers.Mp4
{
    internal record Mp4Movie(TimeBase TimeBase, MediaDuration? Duration,
        Mp4TrackInfo[] Tracks, Mp4SampleTable[] Tables);
}
