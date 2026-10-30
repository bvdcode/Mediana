// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.MpegTs;
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
        private readonly Func<int, CancellationToken, ValueTask<Stream>> openSegment;
        private readonly MediaBufferAllocator allocator;
        private readonly AvcHlsWriteOptions options;
        private readonly H264RandomAccessInspector inspector;
        private readonly List<HlsMediaSegment> segments = [];
        private MpegTsWriter? transport;
        private Stream? destination;
        private long segmentStart;
        private long segmentBytesStart;
        private long? lastEnd;
        private long? audioEnd;
        private long? lastDecodeTime;
        private bool lastWasAudio;
        private bool completed;
        private bool faulted;
        private bool disposed;
        private int busy;

        private AvcHlsWriter(EncodedStreamInfo stream, HlsAudioTrack? audio, Func<int, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, AvcHlsWriteOptions options)
        {
            this.stream = stream;
            this.audio = audio;
            clock = audio is null ? stream.TimeBase : HlsClock.Common(stream.TimeBase, audio.Stream.TimeBase);
            this.openSegment = openSegment;
            this.allocator = allocator;
            this.options = options;
            inspector = new(stream);
            Segments = segments.AsReadOnly();
        }

        public IReadOnlyList<HlsMediaSegment> Segments { get; }
        public long BytesWritten => transport?.BytesWritten ?? segments.Sum(segment => segment.ByteLength);
        public bool IsComplete => completed;

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
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!completed)
                {
                    EnsureWritable();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (transport is null || lastEnd is null || (audio is not null && audioEnd is null))
                    {
                        throw new InvalidOperationException("Every configured HLS track must contain at least one access unit.");
                    }
                    await transport.CompleteAsync(cancellationToken).ConfigureAwait(false);
                    AddSegment(Math.Max(lastEnd.Value, audioEnd ?? lastEnd.Value));
                    await destination!.DisposeAsync().ConfigureAwait(false);
                    destination = null;
                    completed = true;
                }
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Creates a VOD playlist after successful completion. Only video-only output declares independent segments.
        /// Leading audio priming is retained; the playlist starts at the first video PTS and includes any final audio tail.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri)
        {
            if (!completed)
            {
                throw new InvalidOperationException("Complete the HLS presentation before creating its playlist.");
            }
            return HlsVodPlaylist.Create(Segments, static segment => segment.Duration, segmentUri,
                options.MaximumSegmentUriCharacters, independent: audio is null);
        }

        /// <summary>Measures peak/average bitrates from complete TS output. Peak uses the RFC 8216 contiguous-duration window.
        /// Optional dimensions must describe the intended display pixels. Presentations shorter than half TARGETDURATION have no defined peak.</summary>
        public HlsVariant CreateVariant(string playlistUri, int? displayWidth = null, int? displayHeight = null)
        {
            if (!completed)
            {
                throw new InvalidOperationException("Complete the HLS presentation before measuring its variant.");
            }
            HlsUri.Validate(playlistUri, options.MaximumSegmentUriCharacters);
            (long peak, long average) = HlsBandwidth.Calculate(Segments);
            string codecs = "avc1." + Convert.ToHexString(stream.Configuration.Span.Slice(1, 3));
            if (audio is not null)
            {
                codecs += "," + audio.Codecs;
            }
            return new(playlistUri, codecs, peak, average,
                displayWidth, displayHeight);
        }

        private async ValueTask BeginAsync(long start, CancellationToken token)
        {
            int number = transport is null ? 0 : checked(segments.Count + 1);
            if (options.MaximumSegments is int limit && number >= limit)
            {
                throw new NotSupportedException("The HLS presentation exceeds the configured segment count limit.");
            }
            Stream next = await openSegment(number, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The HLS segment factory returned no destination.");
            bool selected = false;
            try
            {
                if (!next.CanWrite || ReferenceEquals(next, destination))
                {
                    throw new ArgumentException("Each HLS segment requires a new writable stream.");
                }
                if (transport is null)
                {
                    transport = MpegTsWriter.Create(next, audio is null ? [stream] : [stream, audio.Stream], allocator, options.Transport);
                }
                else
                {
                    await transport.BeginSegmentAsync(next, token).ConfigureAwait(false);
                    AddSegment(start);
                }
                Stream? previous = destination;
                destination = next;
                selected = true;
                segmentStart = start;
                segmentBytesStart = transport.BytesWritten;
                if (previous is not null)
                {
                    await previous.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (!selected)
                {
                    await next.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private void AddSegment(long end) => segments.Add(new(segments.Count, new(segmentStart, clock),
            new(checked(end - segmentStart), clock), checked(transport!.BytesWritten - segmentBytesStart)));

        private long ExactTicks(MediaTimestamp timestamp)
        {
            MediaTimestamp scaled = timestamp.Rescale(clock, TimestampRounding.TowardZero);
            if (scaled != timestamp)
            {
                throw new NotSupportedException("HLS access-unit timing must be exactly representable in the configured stream clock.");
            }
            return scaled.Value;
        }

        private void EnsureWritable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed || faulted)
            {
                throw new InvalidOperationException("The AVC HLS writer is complete or faulted.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent AVC HLS writer operations are not supported.");
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
                    try
                    {
                        if (transport is not null)
                        {
                            await transport.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (destination is not null)
                        {
                            await destination.DisposeAsync().ConfigureAwait(false);
                            destination = null;
                        }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }
    }
}
