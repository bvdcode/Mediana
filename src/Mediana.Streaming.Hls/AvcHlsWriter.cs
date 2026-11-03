// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Streams one AVC video track and optional AAC/MP3 audio into IDR-aligned MPEG-TS segments.
    /// Requires per-track nonoverlapping PTS = DTS, positive durations and global decode order with video first at ties. Borrows packets and allocator;
    /// owns streams returned by the destination factory. Failure faults the writer; discard partial output.</summary>
    public partial class AvcHlsWriter : IContainerWriter
    {
        private readonly EncodedStreamInfo stream;
        private readonly HlsAudioTrack? audio;
        private readonly TimeBase clock;
        private readonly AvcHlsWriteOptions options;
        private readonly H264RandomAccessInspector inspector;
        private readonly HlsSegmentOutput output;
        private long? lastEnd;
        private long? audioEnd;
        private long? lastDecodeTime;
        private bool lastWasAudio;

        private AvcHlsWriter(EncodedStreamInfo stream, HlsAudioTrack? audio, Func<int, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, AvcHlsWriteOptions options)
        {
            this.stream = stream;
            this.audio = audio;
            clock = audio is null ? stream.TimeBase : HlsClock.Common(stream.TimeBase, audio.Stream.TimeBase);
            this.options = options;
            inspector = new(stream);
            output = new(audio is null ? [stream] : [stream, audio.Stream], clock, openSegment, allocator, options.Transport, options.MaximumSegments);
        }

        public IReadOnlyList<HlsMediaSegment> Segments => output.Segments;
        public long BytesWritten => output.BytesWritten;
        public bool IsComplete => output.IsComplete;

        public static AvcHlsWriter Create(EncodedStreamInfo stream, Func<int, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, AvcHlsWriteOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(openSegment);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            return new(stream, null, openSegment, allocator, options);
        }

        public static AvcHlsWriter Create(EncodedStreamInfo video, EncodedStreamInfo audio,
            Func<int, CancellationToken, ValueTask<Stream>> openSegment, MediaBufferAllocator allocator, AvcHlsWriteOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(video);
            ArgumentNullException.ThrowIfNull(audio);
            ArgumentNullException.ThrowIfNull(openSegment);
            ArgumentNullException.ThrowIfNull(allocator);
            if (video.StreamIndex == audio.StreamIndex)
            {
                throw new ArgumentException("HLS video and audio stream indices must differ.", nameof(audio));
            }
            options ??= new();
            options.Validate();
            return new(video, new HlsAudioTrack(audio), openSegment, allocator, options);
        }

        public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            output.Enter();
            bool success = false;
            try
            {
                output.EnsureNotDisposed();
                if (!IsComplete)
                {
                    output.EnsureWritable();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!output.HasOutput || lastEnd is null || (audio is not null && audioEnd is null))
                    {
                        throw new InvalidOperationException("Every configured HLS track must contain at least one access unit.");
                    }
                    await output.CompleteAsync(Math.Max(lastEnd.Value, audioEnd ?? lastEnd.Value), cancellationToken).ConfigureAwait(false);
                }
                success = true;
            }
            finally
            {
                output.FinishOperation(success);
            }
        }

        /// <summary>Creates a VOD playlist after successful completion. Only video-only output declares independent segments.
        /// Leading audio priming is retained; the playlist starts at the first video PTS and includes any final audio tail.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri)
            => CreateVodPlaylist(segmentUri, options.PlaylistTargetDurationSeconds);

        /// <summary>Creates a completed playlist with an explicit presentation target, replacing the configured value.
        /// Null selects the measured ceiling. Use the same target when measuring the variant.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri, long? targetDurationSeconds)
        {
            output.EnsureComplete();
            return HlsVodPlaylist.Create(Segments, static segment => segment.Duration, segmentUri,
                options.MaximumSegmentUriCharacters, independent: audio is null, targetDurationSeconds: targetDurationSeconds);
        }

        /// <summary>Measures peak/average bitrates from complete TS output. Peak uses the RFC 8216 contiguous-duration window.
        /// Optional dimensions must describe the intended display pixels. Presentations shorter than half TARGETDURATION have no defined peak.</summary>
        public HlsVariant CreateVariant(string playlistUri, int? displayWidth = null, int? displayHeight = null)
            => CreateVariant(playlistUri, displayWidth, displayHeight, options.PlaylistTargetDurationSeconds);

        /// <summary>Measures the variant using the target advertised by its completed playlist.
        /// The explicit value replaces configuration; null selects the measured ceiling.</summary>
        public HlsVariant CreateVariant(string playlistUri, int? displayWidth, int? displayHeight, long? targetDurationSeconds)
        {
            output.EnsureComplete();
            HlsUri.Validate(playlistUri, options.MaximumSegmentUriCharacters);
            (long peak, long average) = HlsBandwidth.Calculate(Segments, targetDurationSeconds);
            string codecs = "avc1." + Convert.ToHexString(stream.Configuration.Span.Slice(1, 3));
            if (audio is not null)
            {
                codecs += "," + audio.Codecs;
            }
            return new(playlistUri, codecs, peak, average,
                displayWidth, displayHeight);
        }

        public async ValueTask DisposeAsync()
        {
            await output.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
