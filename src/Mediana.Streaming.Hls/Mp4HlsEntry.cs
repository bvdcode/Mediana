using Mediana.Containers.MpegTs;

namespace Mediana.Streaming.Hls
{
    internal readonly record struct Mp4HlsEntry(Mp4HlsSegment Segment, MpegTsSegmentState? State);
}
