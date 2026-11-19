// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;

namespace Mediana.Streaming.Hls
{
    /// <summary>Indexes one AVC video track and optional AAC/MP3 track for repeatable MPEG-TS segment output.
    /// Borrows an immutable random-access source. Owns its reader; I/O operations must not overlap.</summary>
    public class Mp4HlsSession : IAsyncDisposable
    {
        private readonly Mp4Reader reader;
        private readonly MediaBufferAllocator allocator;
        private readonly Mp4HlsOptions options;
        private readonly Mp4HlsSegment[] entries;
        private readonly int videoStreamIndex;
        private bool disposed;
        private int busy;

        private Mp4HlsSession(Mp4Reader reader, MediaBufferAllocator allocator, Mp4HlsOptions options,
            EncodedStreamInfo[] streams, Mp4HlsSegment[] entries)
        {
            this.reader = reader;
            this.allocator = allocator;
            this.options = options;
            this.entries = entries;
            videoStreamIndex = streams[0].StreamIndex;
            Streams = Array.AsReadOnly(streams);
            Segments = Array.AsReadOnly(entries);
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        public IReadOnlyList<Mp4HlsSegment> Segments { get; }

        /// <summary>Creates a VOD media playlist using actual presentation intervals and caller-provided segment URIs.
        /// Returns LF-delimited text for UTF-8 encoding. Does not declare independent audio decoding or variants.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri) =>
            HlsVodPlaylist.Create(Segments, static segment => segment.Duration, segmentUri, options.MaximumSegmentUriCharacters);

        /// <summary>Reads container indexes without scanning compressed payloads. Requested segments validate their IDR
        /// and finish complete TS continuity-counter cycles without needing preceding segment bytes.</summary>
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
                    if (audio.Kind != MediaKind.Audio || audio.CodecId is not ("aac" or "mp3"))
                    {
                        throw new NotSupportedException("MP4 HLS sessions require AAC-LC or MPEG-1/2 Layer III for the selected audio track.");
                    }
                    streams = [reader.Streams[videoStreamIndex], audio];
                }
                else
                {
                    streams = [reader.Streams[videoStreamIndex]];
                }
                Mp4AvcSegmentPlan video = await Mp4AvcSegmentPlan.CreateFromIndexAsync(reader, videoStreamIndex, options.Planning, cancellationToken).ConfigureAwait(false);
                Mp4HlsAudioRange[]? audioRanges = audioStreamIndex.HasValue ?
                    Mp4HlsAudioIndex.Create(reader, audioStreamIndex.Value, video.Segments, cancellationToken) : null;
                Mp4HlsSegment[] entries = video.Segments.Select((segment, number) =>
                    new Mp4HlsSegment(segment, audioRanges?[number], number + 1 == video.Segments.Count)).ToArray();
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
                Mp4HlsSegment segment = entries[number];
                MpegTsSegmentTimeline? preceding = null;
                if (number > 0)
                {
                    Mp4HlsSegment previous = entries[number - 1];
                    MediaTimestamp lastVideo = reader.GetSampleInfo(videoStreamIndex, previous.Video.FirstSample + previous.Video.SampleCount - 1, cancellationToken).DecodeTime;
                    MediaTimestamp lastDecode = lastVideo;
                    if (previous.Audio is Mp4HlsAudioRange audio && audio.SampleCount > 0)
                    {
                        MediaTimestamp lastAudio = reader.GetSampleInfo(audio.StreamIndex, audio.FirstSample + audio.SampleCount - 1, cancellationToken).DecodeTime;
                        if (lastAudio.CompareTo(lastDecode) > 0) { lastDecode = lastAudio; }
                    }
                    preceding = new(lastDecode, lastVideo);
                }
                await using MpegTsWriter writer = MpegTsWriter.CreateIndexedSegment(destination, Streams, allocator, preceding, options.Transport);
                await Mp4HlsPacketWriter.CopyAsync(reader, videoStreamIndex, segment, writer, cancellationToken, alignCounters: true).ConfigureAwait(false);
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
