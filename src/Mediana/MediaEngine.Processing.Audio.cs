// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.Processing.Audio;

namespace Mediana
{
    public partial class MediaEngine
    {
        internal async ValueTask<ProcessedMediaTrack> OpenAudioTrackAsync(AudioEncodingRequest request, MediaTrackInput input, CancellationToken token)
        {
            EncodedStreamInfo stream = input.Select(request.InputStreamIndex, MediaKind.Audio);
            MediaDecodePipeline<AudioFrame> pipeline = CreateProcessingDecoder(stream, audioDecoders, input);
            if (input.Selection is MediaTimeRange requested) { await pipeline.SeekAsync(requested.Start, token).ConfigureAwait(false); }
            MediaTimeRange? selection = input.GetSelection(stream.StreamIndex, token);
            Func<CancellationToken, ValueTask<AudioFrame?>> read = ReadDecodedAsync;
            if (selection is MediaTimeRange range)
            {
                AudioTrimmer trimmer = input.Resources.OwnAsync(AudioTrimmer.CreateTimeWindow(read, range.Start, range.End, input.Allocator));
                read = trimmer.ReadFrameAsync;
            }
            using AudioFrame first = await read(token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The selected audio interval contains no samples.");
            AudioEncodingPcm converter = new(first.Format, request.Channels);
            AudioFrame? opening = input.Resources.Own(converter.Apply(first, input.Allocator, token));
            MediaTimestamp timestamp = opening.Timestamp ?? throw new NotSupportedException("Audio encoding requires known sample timestamps.");
            TimeBase clock = TimeBase.Common(timestamp.ReduceClock().TimeBase, new(1, opening.Format.SampleRate));
            IMediaEncoder<AudioFrame> encoder = input.Resources.Own(request.Encoder.Create(opening.Format, clock, input.Allocator, request.OutputStreamIndex));
            long frames = 0;
            long samples = 0;
            MediaEncodeSource<AudioFrame> source = new(ReadPreparedAsync, encoder);
            return new(source, () => new(stream.StreamIndex, encoder.Stream, frames, samples, 0, 0,
                (encoder as IAudioEncoderTiming)?.PrimingSamples, (encoder as IAudioEncoderTiming)?.PaddingSamples));

            async ValueTask<AudioFrame?> ReadDecodedAsync(CancellationToken cancellationToken)
            {
                input.Budget.CountFrameRead();
                return await pipeline.ReadAsync(cancellationToken).ConfigureAwait(false);
            }

            async ValueTask<AudioFrame?> ReadPreparedAsync(CancellationToken cancellationToken)
            {
                AudioFrame? result;
                if (opening is not null) { result = opening; opening = null; }
                else
                {
                    using AudioFrame? frame = await read(cancellationToken).ConfigureAwait(false);
                    result = frame is null ? null : converter.Apply(frame, input.Allocator, cancellationToken);
                }
                if (result is not null)
                {
                    frames = checked(frames + 1);
                    samples = checked(samples + result.SamplesPerChannel);
                }
                return result;
            }
        }
    }
}
