using Mediana.Core;

namespace Mediana.Containers.MpegTs
{
    internal readonly record struct MpegTsTrackState(EncodedStreamInfo Stream, int Counter, long? LastDecodeTime);
}
