using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal record Mp4Movie(TimeBase TimeBase, MediaDuration? Duration, VideoDisplayMatrix DisplayMatrix,
        Mp4TrackInfo[] Tracks, Mp4SampleTable[] Tables);
}
