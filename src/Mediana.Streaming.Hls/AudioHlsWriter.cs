// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    /// <summary>Streams one continuous AAC-LC or MPEG-1/2 Layer III track into MPEG-TS segments.
    /// Boundaries select the first packet at or after each TargetDuration interval from the initial PTS.
    /// Requires contiguous PTS = DTS. Durations follow complete coded frames, without container presentation clipping.
    /// Borrows packets and allocator; owns factory destinations.</summary>
    public class AudioHlsWriter : IContainerWriter
    {
        private readonly HlsAudioTrack audio;
        private readonly HlsWriteOptions options;
        private readonly HlsSegmentOutput output;
        private readonly long frameTicks;
        private long? firstStart;
        private long? lastEnd;

        private AudioHlsWriter(EncodedStreamInfo stream, Func<int, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, HlsWriteOptions options)
            : this(stream, (number, _, token) => openSegment(number, token), allocator, options, false)
        {
        }

        private AudioHlsWriter(EncodedStreamInfo stream,
            Func<int, MpegTsSegmentState?, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, HlsWriteOptions options, bool captureState = true)
        {
            audio = new(stream);
            frameTicks = HlsClock.ExactTicks(new(audio.CodedDuration.Value, audio.CodedDuration.TimeBase), stream.TimeBase);
            this.options = options;
            output = new([stream], stream.TimeBase, openSegment, allocator, options.Transport, options.MaximumSegments, captureState);
        }

        public IReadOnlyList<HlsMediaSegment> Segments => output.Segments;
        public long BytesWritten => output.BytesWritten;
        public bool IsComplete => output.IsComplete;
        public string Codecs => audio.Codecs;

        public static AudioHlsWriter Create(EncodedStreamInfo stream, Func<int, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, HlsWriteOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(openSegment);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            return new(stream, openSegment, allocator, options);
        }

        /// <summary>Supplies immutable preceding transport state to the destination factory; segment zero receives null.
        /// Completed segments can be recreated from their original packets after disposal using ResumeSegment.
        /// The state retains no media buffers and does not make audio independently decodable or restore encoder history.</summary>
        public static AudioHlsWriter Create(EncodedStreamInfo stream,
            Func<int, MpegTsSegmentState?, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, HlsWriteOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(openSegment);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            options.Validate();
            return new(stream, openSegment, allocator, options);
        }

        public async ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            output.Enter();
            bool success = false;
            try
            {
                output.EnsureWritable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                if (packet.StreamIndex != audio.Stream.StreamIndex || packet.PresentationTime is not MediaTimestamp pts ||
                    packet.DecodeTime is not MediaTimestamp dts || pts != dts)
                {
                    throw new NotSupportedException("Audio HLS writing requires the configured stream and known PTS = DTS.");
                }
                TimeBase clock = audio.Stream.TimeBase;
                long start = HlsClock.ExactTicks(pts, clock);
                long end = checked(start + frameTicks);
                if (lastEnd.HasValue && start != lastEnd.Value)
                {
                    throw new InvalidDataException("Audio HLS access units require a contiguous presentation timeline.");
                }
                if (!firstStart.HasValue || BoundaryReached(start))
                {
                    await output.BeginAsync(start, cancellationToken).ConfigureAwait(false);
                }
                await output.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                firstStart ??= start;
                lastEnd = end;
                success = true;
            }
            finally
            {
                output.FinishOperation(success);
            }
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
                    if (lastEnd is not long end)
                    {
                        throw new InvalidOperationException("The HLS audio track must contain at least one access unit.");
                    }
                    await output.CompleteAsync(end, cancellationToken).ConfigureAwait(false);
                }
                success = true;
            }
            finally
            {
                output.FinishOperation(success);
            }
        }

        /// <summary>Creates a VOD playlist after completion. Codec history, encoder priming and padding remain in the coded stream;
        /// the playlist does not declare independent segments.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri)
            => CreateVodPlaylist(segmentUri, options.PlaylistTargetDurationSeconds);

        /// <summary>Creates a completed playlist with an explicit presentation target, replacing the configured value.
        /// Null selects the measured ceiling. Use the same target when measuring the audio rendition.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri, long? targetDurationSeconds)
        {
            output.EnsureComplete();
            return HlsVodPlaylist.Create(Segments, static segment => segment.Duration, segmentUri, options.MaximumSegmentUriCharacters,
                targetDurationSeconds: targetDurationSeconds);
        }

        /// <summary>Measures complete transport output for an external audio group. Selection attributes can be set on the returned record.
        /// Presentations shorter than half TARGETDURATION have no defined peak bandwidth.</summary>
        public HlsAudioRendition CreateRendition(string playlistUri, string groupId, string name)
            => CreateRendition(playlistUri, groupId, name, options.PlaylistTargetDurationSeconds);

        /// <summary>Measures audio using the target advertised by its completed playlist.
        /// The explicit value replaces configuration; null selects the measured ceiling.</summary>
        public HlsAudioRendition CreateRendition(string playlistUri, string groupId, string name, long? targetDurationSeconds)
        {
            output.EnsureComplete();
            HlsUri.Validate(playlistUri, options.MaximumSegmentUriCharacters);
            HlsAttribute.Validate(playlistUri);
            HlsAttribute.Validate(groupId);
            HlsAttribute.Validate(name);
            (long peak, long average) = HlsBandwidth.Calculate(Segments, targetDurationSeconds);
            return new(playlistUri, groupId, name, Codecs, peak, average, audio.Channels);
        }

        public async ValueTask DisposeAsync()
        {
            await output.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        private bool BoundaryReached(long start)
        {
            TimeBase clock = audio.Stream.TimeBase;
            MediaDuration target = options.TargetDuration;
            Int128 elapsed = ((Int128)start - firstStart!.Value) * clock.Numerator * target.TimeBase.Denominator;
            Int128 interval = (Int128)target.Value * target.TimeBase.Numerator * clock.Denominator;
            return elapsed / interval >= (long)Segments.Count + 1;
        }
    }
}
