// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.Processing;

namespace Mediana
{
    public partial class MediaEngine
    {
        internal async ValueTask<ProcessedMediaTrack> OpenVideoTrackAsync(VideoEncodingRequest request, MediaTrackInput input, CancellationToken token)
        {
            EncodedStreamInfo stream = input.Select(request.InputStreamIndex, MediaKind.Video);
            MediaTimeRange? selection = input.GetSelection(stream.StreamIndex, token);
            TimeBase outputClock;
            if (request.OutputTimeBase is TimeBase declared) { outputClock = declared; }
            else
            {
                if (input.Reader is not IIndexedPacketTimingReader clocks)
                { throw new NotSupportedException("Automatic video clock selection requires indexed presentation clocks."); }
                outputClock = VideoSegmentPlanner.GetTimeBase(clocks, stream.StreamIndex, token);
                if (selection is MediaTimeRange selected)
                { outputClock = TimeBase.Common(outputClock, selected.End.ReduceClock().TimeBase); }
            }
            MediaDecodePipeline<VideoFrame> pipeline = CreateProcessingDecoder(stream, decoders, input);
            if (input.Selection is MediaTimeRange requested) { await pipeline.SeekAsync(requested.Start, token).ConfigureAwait(false); }
            VideoPresentationReader presentation = input.Resources.OwnAsync(new VideoPresentationReader(ReadDecodedAsync, selection, request.IntervalAlignment));
            using VideoFrame first = await presentation.ReadFrameAsync(token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The selected video interval contains no frames.");
            VideoDisplayMatrix matrix = stream.DisplayMatrix ?? throw new NotSupportedException("Video encoding requires a declared display transform.");
            VideoPreparation preparation = input.Resources.Own(new VideoPreparation(first.Format, matrix, input.Allocator, request.Preparation));
            IMediaEncoder<VideoFrame> encoder = input.Resources.Own(request.Encoder.Create(preparation.OutputFormat, outputClock, input.Allocator, request.OutputStreamIndex));
            VideoFrame? opening = input.Resources.Own(first.Retain());
            long frames = 0;
            MediaEncodeSource<VideoFrame> source = new(ReadPreparedAsync, encoder);
            return new(source, () => new(stream.StreamIndex, encoder.Stream, frames, 0, 0, 0, null, null));

            async ValueTask<VideoFrame?> ReadDecodedAsync(CancellationToken cancellationToken)
            {
                input.Budget.CountFrameRead();
                return await pipeline.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            async ValueTask<VideoFrame?> ReadPreparedAsync(CancellationToken cancellationToken)
            {
                using VideoFrame? frame = opening ?? await presentation.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                opening = null;
                if (frame is null) { return null; }
                VideoFrame result = preparation.Apply(frame, cancellationToken);
                frames = checked(frames + 1);
                return result;
            }
        }
    }
}
