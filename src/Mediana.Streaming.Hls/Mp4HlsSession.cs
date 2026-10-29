using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Streaming.Hls
{
    /// <summary>Indexes one AVC video track and optional AAC track for repeatable MPEG-TS segment output.
    /// Borrows an immutable random-access source. Owns its reader; I/O operations must not overlap.</summary>
    public class Mp4HlsSession : IAsyncDisposable
    {
        private readonly Mp4Reader reader;
        private readonly MediaBufferAllocator allocator;
        private readonly Mp4HlsOptions options;
        private readonly Mp4HlsEntry[] entries;
        private readonly int videoStreamIndex;
        private bool disposed;
        private int busy;

        private Mp4HlsSession(Mp4Reader reader, MediaBufferAllocator allocator, Mp4HlsOptions options,
            EncodedStreamInfo[] streams, Mp4HlsEntry[] entries)
        {
            this.reader = reader;
            this.allocator = allocator;
            this.options = options;
            this.entries = entries;
            videoStreamIndex = streams[0].StreamIndex;
            Streams = Array.AsReadOnly(streams);
            Segments = Array.AsReadOnly(entries.Select(entry => entry.Segment).ToArray());
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        public IReadOnlyList<Mp4HlsSegment> Segments { get; }

        /// <summary>Creates a VOD media playlist using actual presentation intervals and caller-provided segment URIs.
        /// Returns LF-delimited text for UTF-8 encoding. Does not declare independent audio decoding or variants.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri) =>
            HlsVodPlaylist.Create(Segments, segmentUri, options.MaximumSegmentUriCharacters);

        /// <summary>Inspects IDR candidates and scans the selected compressed packets once to establish transport state.
        /// Retains bounded segment metadata and reader metadata, without storing media payloads or output segments.</summary>
        public static async ValueTask<Mp4HlsSession> OpenAsync(IRandomAccessByteSource source, MediaBufferAllocator allocator,
            int videoStreamIndex, int? audioStreamIndex = null, Mp4HlsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(videoStreamIndex);
            if (audioStreamIndex.HasValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(audioStreamIndex.Value);
                if (audioStreamIndex == videoStreamIndex)
                {
                    throw new ArgumentException("Video and audio stream indices must differ.", nameof(audioStreamIndex));
                }
            }
            options ??= new();
            options.Validate();
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, options.Reading, cancellationToken).ConfigureAwait(false);
            bool success = false;
            try
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(videoStreamIndex, reader.Streams.Count);
                EncodedStreamInfo[] streams;
                if (audioStreamIndex.HasValue)
                {
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(audioStreamIndex.Value, reader.Streams.Count);
                    EncodedStreamInfo audio = reader.Streams[audioStreamIndex.Value];
                    if (audio.Kind != MediaKind.Audio || audio.CodecId != "aac")
                    {
                        throw new NotSupportedException("MP4 HLS sessions require AAC-LC for the selected audio track.");
                    }
                    streams = [reader.Streams[videoStreamIndex], audio];
                }
                else
                {
                    streams = [reader.Streams[videoStreamIndex]];
                }
                Mp4AvcSegmentPlan video = await Mp4AvcSegmentPlan.CreateAsync(reader, videoStreamIndex, options.Planning, cancellationToken).ConfigureAwait(false);
                Mp4HlsAudioRange[]? audioRanges = audioStreamIndex.HasValue ?
                    Mp4HlsAudioIndex.Create(reader, audioStreamIndex.Value, video.Segments, cancellationToken) : null;
                Mp4HlsEntry[] entries = new Mp4HlsEntry[video.Segments.Count];
                MpegTsSegmentState? state = null;
                for (int number = 0; number < entries.Length; number++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Mp4HlsSegment segment = new(video.Segments[number], audioRanges?[number], number + 1 == entries.Length);
                    entries[number] = new(segment, state);
                    await using MpegTsWriter writer = state is null ? MpegTsWriter.Create(Stream.Null, streams, allocator, options.Transport) :
                        MpegTsWriter.ResumeSegment(Stream.Null, state, allocator);
                    await Mp4HlsPacketWriter.CopyAsync(reader, videoStreamIndex, segment, writer, cancellationToken).ConfigureAwait(false);
                    if (number + 1 < entries.Length)
                    {
                        state = writer.CaptureSegmentState();
                    }
                    await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Mp4HlsSession session = new(reader, allocator, options, streams, entries);
                success = true;
                return session;
            }
            finally
            {
                if (!success)
                {
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Writes only the indexed packets of one segment and flushes the borrowed destination.
        /// Failed or cancelled output must be discarded. The immutable index remains usable for a retry.</summary>
        public async ValueTask WriteSegmentAsync(int number, Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(number);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, entries.Length);
                ArgumentNullException.ThrowIfNull(destination);
                cancellationToken.ThrowIfCancellationRequested();
                Mp4HlsEntry entry = entries[number];
                await using MpegTsWriter writer = entry.State is null ? MpegTsWriter.Create(destination, Streams, allocator, options.Transport) :
                    MpegTsWriter.ResumeSegment(destination, entry.State, allocator);
                await Mp4HlsPacketWriter.CopyAsync(reader, videoStreamIndex, entry.Segment, writer, cancellationToken).ConfigureAwait(false);
                await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    await reader.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent MP4 HLS session operations are not supported.");
            }
        }
    }
}
