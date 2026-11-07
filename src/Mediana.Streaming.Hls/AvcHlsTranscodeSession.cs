// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.Processing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Encodes requested AVC video windows with optional copied or encoded AAC audio and no preceding encoding pass.
    /// Borrows the indexed reader and allocator. Calls, disposal and other reader operations must not overlap.
    /// Each request owns fresh decoder and encoder state and buffers only that segment's compressed output.</summary>
    public class AvcHlsTranscodeSession : IAsyncDisposable
    {
        private readonly IIndexedContainerReader reader;
        private readonly MediaEngine engine;
        private readonly MediaBufferAllocator allocator;
        private readonly H264EncoderOptions options;
        private readonly VideoPreparationOptions preparationOptions;
        private readonly EncodedStreamInfo inputStream;
        private readonly Mp4VideoTrackDefinition track;
        private readonly IHlsAudioTrack? audio;
        private readonly Mp4TrackDefinition[] tracks;
        private bool disposed;
        private int busy;

        private AvcHlsTranscodeSession(IIndexedContainerReader reader, IVideoDecoderFactory decoderFactory,
            MediaBufferAllocator allocator,
            H264EncoderOptions options, VideoPreparationOptions preparationOptions,
            EncodedStreamInfo inputStream, HlsVideoWindow[] windows, Mp4VideoTrackDefinition track, IHlsAudioTrack? audio,
            IAudioDecoderFactory? audioDecoder)
        {
            this.reader = reader;
            MediaEngineBuilder builder = new MediaEngineBuilder().AddVideoDecoder(decoderFactory);
            if (audioDecoder is not null) { builder.AddAudioDecoder(audioDecoder); }
            engine = builder.Build();
            this.allocator = allocator;
            this.options = options;
            this.preparationOptions = preparationOptions;
            this.inputStream = inputStream;
            this.track = track;
            this.audio = audio;
            tracks = audio is null ? [track] : [track, audio.Definition];
            Streams = Array.AsReadOnly(tracks.Select(item => item.Stream).ToArray());
            Windows = Array.AsReadOnly(windows);
        }

        public IReadOnlyList<HlsVideoWindow> Windows { get; }
        public EncodedStreamInfo Stream => track.Stream;
        public IReadOnlyList<EncodedStreamInfo> Streams { get; }

        /// <summary>Validates declarations and creates codec configuration without reading compressed source packets.
        /// The caller supplies indexed, contiguous, frame-aligned windows and the decoder's input format.</summary>
        public static AvcHlsTranscodeSession Create(IIndexedContainerReader reader, int sourceStreamIndex,
            IVideoDecoderFactory decoderFactory, VideoFormat format, IReadOnlyList<HlsVideoWindow> windows,
            MediaBufferAllocator allocator, H264EncoderOptions? options = null, VideoPreparationOptions? preparation = null,
            int? copyAudioStreamIndex = null, HlsAacEncoding? audioEncoding = null)
        {
            ArgumentNullException.ThrowIfNull(windows);
            return CreateCore(reader, sourceStreamIndex, decoderFactory, format, windows.ToArray(), allocator, options, preparation, null, copyAudioStreamIndex, audioEncoding);
        }

        /// <summary>Plans whole-video windows from indexed presentation clocks, without a decoding or encoding pass.
        /// Uses the same generic planner exposed by container sessions; target multiples align to actual presentation units.</summary>
        public static AvcHlsTranscodeSession Create(IIndexedPacketTimingReader reader, int sourceStreamIndex,
            IVideoDecoderFactory decoderFactory, VideoFormat format, MediaDuration targetDuration,
            MediaBufferAllocator allocator, H264EncoderOptions? options = null, VideoPreparationOptions? preparation = null,
            int? copyAudioStreamIndex = null, HlsAacEncoding? audioEncoding = null)
        {
            using VideoSegmentPlan plan = VideoSegmentPlanner.Create(reader, sourceStreamIndex, targetDuration, allocator);
            HlsVideoWindow[] windows = new HlsVideoWindow[plan.Count];
            for (int index = 0; index < windows.Length; index++)
            {
                MediaTimeRange interval = plan[index];
                windows[index] = new(index, interval.Start, interval.Duration);
            }
            return CreateCore(reader, sourceStreamIndex, decoderFactory, format, windows, allocator, options, preparation, plan[0].Start.TimeBase, copyAudioStreamIndex, audioEncoding);
        }

        private static AvcHlsTranscodeSession CreateCore(IIndexedContainerReader reader, int sourceStreamIndex,
            IVideoDecoderFactory decoderFactory, VideoFormat format, HlsVideoWindow[] windows,
            MediaBufferAllocator allocator, H264EncoderOptions? options, VideoPreparationOptions? preparation, TimeBase? outputClock,
            int? copyAudioStreamIndex, HlsAacEncoding? audioEncoding)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(decoderFactory);
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(windows);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(sourceStreamIndex);
            if (copyAudioStreamIndex is not null && audioEncoding is not null)
            { throw new ArgumentException("Select either audio copy or audio encoding.", nameof(audioEncoding)); }
            EncodedStreamInfo source = reader.Streams.SingleOrDefault(stream => stream.StreamIndex == sourceStreamIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(sourceStreamIndex));
            if (source.DisplayMatrix is null) { throw new NotSupportedException("On-demand video encoding requires an explicit source display transform."); }
            if (decoderFactory.GetUnsupportedReason(source) is string reason) { throw new NotSupportedException(reason); }
            if (decoderFactory is not IRandomAccessDecoderFactory)
            {
                throw new NotSupportedException("On-demand video encoding requires decoder restart inspection.");
            }
            ValidateWindows(windows);
            options ??= new();
            preparation = preparation is null ? new() : preparation with { };
            using VideoPreparation prepared = new(format, source.DisplayMatrix.Value, allocator, preparation);
            using H264Encoder encoder = H264Encoder.Create(prepared.OutputFormat, outputClock ?? source.TimeBase, allocator, options);
            EncodedStreamInfo encoded = encoder.Stream;
            EncodedStreamInfo output = new(encoded.StreamIndex, encoded.Kind, encoded.CodecId, encoded.Format,
                encoded.TimeBase, encoded.Configuration.Span, VideoDisplayMatrix.Identity);
            Mp4VideoTrackDefinition track = new(output, prepared.OutputFormat);
            IHlsAudioTrack? audio = null;
            if (copyAudioStreamIndex is int audioIndex)
            {
                if (reader is not IIndexedPacketTimingReader timing)
                { throw new NotSupportedException("HLS audio copy requires indexed packet clocks."); }
                audio = new HlsCopiedAudioTrack(timing, audioIndex, 1, 2);
            }
            if (audioEncoding is not null) { audio = new HlsEncodedAudioTrack(reader, audioEncoding, allocator, 1, 2); }
            return new(reader, decoderFactory, allocator, options, preparation, source, windows, track, audio, audioEncoding?.Decoder);
        }

        public string CreateVodPlaylist(string initializationUri, Func<int, string> segmentUri,
            int maximumUriCharacters = 4096)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUriCharacters);
            return HlsVodPlaylist.Create(Windows, static window => window.Duration, segmentUri,
                maximumUriCharacters, independent: audio is null, initializationUri: initializationUri);
        }

        public async ValueTask WriteInitializationAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                await Mp4EncodedFragmentWriter.WriteInitializationAsync(tracks, destination, allocator, cancellationToken).ConfigureAwait(false);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        /// <summary>Seeks through the existing index and encodes only the selected window after necessary decoder preroll.
        /// Any failed output must be discarded. Cancellation and output failures do not change later requests.</summary>
        public async ValueTask<HlsMediaSegment> WriteSegmentAsync(int number, Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(number);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, Windows.Count);
                ArgumentNullException.ThrowIfNull(destination);
                if (!destination.CanWrite) { throw new ArgumentException("The segment destination must be writable.", nameof(destination)); }
                cancellationToken.ThrowIfCancellationRequested();
                HlsVideoWindow window = Windows[number];
                List<MediaTrackRequest> requests = [new VideoEncodingRequest(inputStream.StreamIndex, track.Stream.StreamIndex,
                    new H264EncoderFactory(options), preparationOptions, track.TimeBase, VideoIntervalAlignment.CompleteFrames)];
                if (audio?.CreateRequest(window) is MediaTrackRequest audioRequest) { requests.Add(audioRequest); }
                await using MediaProcessingSession processing = await engine.OpenProcessingAsync(reader, requests, allocator,
                    new() { Selection = new(window.Start, window.End) }, cancellationToken).ConfigureAwait(false);
                foreach (EncodedStreamInfo stream in processing.Streams)
                {
                    EncodedStreamInfo expected = Streams.Single(item => item.StreamIndex == stream.StreamIndex);
                    if (stream.TimeBase != expected.TimeBase || !stream.Configuration.Span.SequenceEqual(expected.Configuration.Span))
                    { throw new InvalidOperationException("The encoder configuration changed between segment requests."); }
                }
                await using HlsFragmentPacketBuffer output = new(track, tracks, destination, allocator, checked((uint)number + 1), window);
                await processing.WriteAsync(output, cancellationToken).ConfigureAwait(false);
                return new(number, window.Start, window.Duration, output.BytesWritten);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private static void ValidateWindows(HlsVideoWindow[] windows)
        {
            if (windows.Length == 0) { throw new ArgumentException("HLS requires at least one video window.", nameof(windows)); }
            for (int index = 0; index < windows.Length; index++)
            {
                HlsVideoWindow window = windows[index];
                window.Start.TimeBase.Validate();
                window.Duration.TimeBase.Validate();
                if (window.Number != index || window.Start.Value < 0 || window.Duration.Value <= 0 ||
                    window.Start.TimeBase != window.Duration.TimeBase || window.Start.TimeBase != windows[0].Start.TimeBase ||
                    (index > 0 && windows[index - 1].End != window.Start))
                {
                    throw new ArgumentException("HLS windows require sequential numbers and contiguous positive durations on one common clock.", nameof(windows));
                }
                _ = window.End;
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; }
            finally { Volatile.Write(ref busy, 0); }
            return ValueTask.CompletedTask;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent on-demand HLS video operations are not supported.");
            }
        }
    }
}
