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
    /// <summary>Encodes requested video windows to independent AVC fMP4 segments with no preceding encoding pass.
    /// Borrows the indexed reader and allocator. Calls, disposal and other reader operations must not overlap.
    /// Each request owns fresh decoder and encoder state and buffers only that segment's compressed output.</summary>
    public class AvcHlsTranscodeSession : IAsyncDisposable
    {
        private readonly IIndexedContainerReader reader;
        private readonly IVideoDecoderFactory decoderFactory;
        private readonly IRandomAccessDecoderFactory restartFactory;
        private readonly VideoFormat format;
        private readonly MediaBufferAllocator allocator;
        private readonly H264EncoderOptions options;
        private readonly VideoPreparationOptions preparationOptions;
        private readonly int sourceStreamIndex;
        private readonly Mp4VideoTrackDefinition track;
        private bool disposed;
        private int busy;

        private AvcHlsTranscodeSession(IIndexedContainerReader reader, IVideoDecoderFactory decoderFactory,
            IRandomAccessDecoderFactory restartFactory, VideoFormat format, MediaBufferAllocator allocator,
            H264EncoderOptions options, VideoPreparationOptions preparationOptions,
            int sourceStreamIndex, HlsVideoWindow[] windows, Mp4VideoTrackDefinition track)
        {
            this.reader = reader;
            this.decoderFactory = decoderFactory;
            this.restartFactory = restartFactory;
            this.format = format;
            this.allocator = allocator;
            this.options = options;
            this.preparationOptions = preparationOptions;
            this.sourceStreamIndex = sourceStreamIndex;
            this.track = track;
            Windows = Array.AsReadOnly(windows);
        }

        public IReadOnlyList<HlsVideoWindow> Windows { get; }
        public EncodedStreamInfo Stream => track.Stream;

        /// <summary>Validates declarations and creates codec configuration without reading compressed source packets.
        /// The caller supplies indexed, contiguous, frame-aligned windows and the decoder's input format.</summary>
        public static AvcHlsTranscodeSession Create(IIndexedContainerReader reader, int sourceStreamIndex,
            IVideoDecoderFactory decoderFactory, VideoFormat format, IReadOnlyList<HlsVideoWindow> windows,
            MediaBufferAllocator allocator, H264EncoderOptions? options = null, VideoPreparationOptions? preparation = null)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(decoderFactory);
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(windows);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(sourceStreamIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(sourceStreamIndex, reader.Streams.Count);
            EncodedStreamInfo source = reader.Streams[sourceStreamIndex];
            if (source.DisplayMatrix is null) { throw new NotSupportedException("On-demand video encoding requires an explicit source display transform."); }
            if (decoderFactory.GetUnsupportedReason(source) is string reason) { throw new NotSupportedException(reason); }
            if (decoderFactory is not IRandomAccessDecoderFactory restart)
            {
                throw new NotSupportedException("On-demand video encoding requires decoder restart inspection.");
            }
            HlsVideoWindow[] snapshot = windows.ToArray();
            ValidateWindows(snapshot);
            options ??= new();
            preparation = preparation is null ? new() : preparation with { };
            using VideoPreparation prepared = new(format, source.DisplayMatrix.Value, allocator, preparation);
            using H264Encoder encoder = H264Encoder.Create(prepared.OutputFormat, source.TimeBase, allocator, options);
            EncodedStreamInfo encoded = encoder.Stream;
            EncodedStreamInfo output = new(encoded.StreamIndex, encoded.Kind, encoded.CodecId, encoded.Format,
                encoded.TimeBase, encoded.Configuration.Span, VideoDisplayMatrix.Identity);
            Mp4VideoTrackDefinition track = new(output, prepared.OutputFormat);
            return new(reader, decoderFactory, restart, format, allocator, options, preparation, sourceStreamIndex, snapshot, track);
        }

        public string CreateVodPlaylist(string initializationUri, Func<int, string> segmentUri,
            int maximumUriCharacters = 4096)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumUriCharacters);
            return HlsVodPlaylist.Create(Windows, static window => window.Duration, segmentUri,
                maximumUriCharacters, independent: true, initializationUri: initializationUri);
        }

        public async ValueTask WriteInitializationAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                await Mp4VideoFragmentWriter.WriteInitializationAsync(track, destination, allocator, cancellationToken).ConfigureAwait(false);
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
                MediaTimestamp end = Exact(window.Start, track.TimeBase);
                MediaTimestamp limit = Exact(window.End, track.TimeBase);
                using MediaDecodePipeline<VideoFrame> decoding = CreateDecoder();
                await decoding.SeekAsync(window.Start, cancellationToken).ConfigureAwait(false);
                await using VideoTrimmer selected = new(decoding.ReadAsync, window.Start, window.End);
                using VideoPreparation preparation = new(format, reader.Streams[sourceStreamIndex].DisplayMatrix!.Value, allocator, preparationOptions);
                using H264Encoder encoder = H264Encoder.Create(preparation.OutputFormat, reader.Streams[sourceStreamIndex].TimeBase, allocator, options);
                if (!encoder.Stream.Configuration.Span.SequenceEqual(Stream.Configuration.Span))
                {
                    throw new InvalidOperationException("The AVC encoder configuration changed between segment requests.");
                }
                await using HlsFragmentPacketBuffer output = new(track, destination, allocator, checked((uint)number + 1));
                MediaEncodePipeline<VideoFrame> encoding = new(encoder, output);
                while (end.CompareTo(limit) < 0)
                {
                    using VideoFrame frame = await selected.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("The source ended before the requested video window was complete.");
                    if (frame.Timestamp != end || frame.Duration is not MediaDuration duration || duration.Value == 0)
                    {
                        throw new InvalidDataException("HLS video windows require contiguous frames beginning at the requested timestamp.");
                    }
                    MediaTimestamp length = new(duration.Value, duration.TimeBase);
                    MediaTimestamp ticks = Exact(length, end.TimeBase);
                    end = new(checked(end.Value + ticks.Value), end.TimeBase);
                    if (end.CompareTo(limit) > 0) { throw new NotSupportedException("The HLS window end must coincide with a video frame boundary."); }
                    using VideoFrame prepared = preparation.Apply(frame, cancellationToken);
                    await encoding.WriteAsync(prepared, cancellationToken).ConfigureAwait(false);
                }
                await encoding.CompleteInputAsync(cancellationToken).ConfigureAwait(false);
                await output.CompleteAsync(cancellationToken).ConfigureAwait(false);
                return new(number, window.Start, window.Duration, output.BytesWritten);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private static MediaTimestamp Exact(MediaTimestamp value, TimeBase timeBase)
        {
            MediaTimestamp normalized = value.Rescale(timeBase, TimestampRounding.TowardZero);
            if (value != normalized) { throw new NotSupportedException("Video timing must be exact on the output track clock."); }
            return normalized;
        }

        private MediaDecodePipeline<VideoFrame> CreateDecoder()
        {
            EncodedStreamInfo stream = reader.Streams[sourceStreamIndex];
            IRandomAccessInspector inspector = restartFactory.CreateRandomAccessInspector(stream);
            IMediaCodec<MediaPacket, VideoFrame> decoder = decoderFactory.Create(stream, allocator);
            bool created = false;
            try
            {
                MediaDecodePipeline<VideoFrame> result = new(reader, decoder, sourceStreamIndex, randomAccessInspector: inspector);
                created = true;
                return result;
            }
            finally { if (!created) { decoder.Dispose(); } }
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
