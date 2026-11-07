// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana
{
    public partial class MediaEngine
    {
        internal async ValueTask<ProcessedMediaTrack> OpenCopyTrackAsync(PacketCopyRequest request, MediaTrackInput input, CancellationToken token)
        {
            EncodedStreamInfo stream = input.Select(request.InputStreamIndex);
            IndexedPacketRange range = new(0, input.Reader.GetPacketCount(stream.StreamIndex));
            if (input.Selection is MediaTimeRange selection)
            {
                if (input.Reader is not IIndexedPacketTimingReader timing)
                { throw new NotSupportedException("Interval copy requires indexed packet clocks."); }
                IRandomAccessInspector? inspector = null;
                if (stream.Kind == MediaKind.Video)
                {
                    IDecoderFactory<Mediana.Core.Video.VideoFrame> factory = SelectDecoder(stream, decoders);
                    inspector = (factory as IRandomAccessDecoderFactory)?.CreateRandomAccessInspector(stream)
                        ?? throw new NotSupportedException("Video interval copy requires codec restart inspection.");
                }
                range = await IndexedCopyPlanner.CreateAsync(timing, stream.StreamIndex, selection, inspector, input.Budget, token).ConfigureAwait(false);
            }
            IndexedCopySource source = new(input.Reader, stream.StreamIndex, request.OutputStreamIndex, range.FirstPacket, range.PacketCount, input.Budget);
            return new(source, () => new(stream.StreamIndex, source.Stream, 0, 0, 0, 0, null, null));
        }
    }
}
