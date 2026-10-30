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
    /// <summary>Streams one AVC video track into IDR-aligned MPEG-TS segments.
    /// Requires nonoverlapping PTS = DTS with positive durations. Borrows packets and allocator;
    /// owns streams returned by the destination factory. Failure faults the writer; discard partial output.</summary>
    public class AvcHlsWriter : IContainerWriter
    {
        private readonly EncodedStreamInfo stream;
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
        private bool completed;
        private bool faulted;
        private bool disposed;
        private int busy;

        private AvcHlsWriter(EncodedStreamInfo stream, Func<int, CancellationToken, ValueTask<Stream>> openSegment,
            MediaBufferAllocator allocator, AvcHlsWriteOptions options)
        {
            this.stream = stream;
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
            return new(stream, openSegment, allocator, options);
        }

        public async ValueTask WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureWritable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                if (packet.StreamIndex != stream.StreamIndex || packet.PresentationTime is not MediaTimestamp pts ||
                    packet.DecodeTime is not MediaTimestamp dts || pts != dts || packet.Duration is not MediaDuration duration || duration.Value == 0)
                {
                    throw new NotSupportedException("AVC HLS writing requires the configured video stream, known PTS = DTS and a positive duration.");
                }
                long start = ExactTicks(pts);
                long length = ExactTicks(new(duration.Value, duration.TimeBase));
                long end = checked(start + length);
                if (lastEnd > start)
                {
                    throw new InvalidDataException("AVC HLS access units must not overlap or move backwards in presentation time.");
                }
                bool boundary = transport is null || new MediaTimestamp(checked(start - segmentStart), stream.TimeBase)
                    .CompareTo(new(options.TargetDuration.Value, options.TargetDuration.TimeBase)) >= 0;
                if (boundary && inspector.IsIdr(packet, cancellationToken))
                {
                    await BeginAsync(start, cancellationToken).ConfigureAwait(false);
                }
                if (transport is null)
                {
                    throw new InvalidDataException("The first AVC HLS packet must contain an IDR access unit.");
                }
                await transport.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
                lastEnd = end;
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
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
                    if (transport is null || lastEnd is null)
                    {
                        throw new InvalidOperationException("An empty AVC HLS presentation cannot be completed.");
                    }
                    await transport.CompleteAsync(cancellationToken).ConfigureAwait(false);
                    AddSegment(lastEnd.Value);
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

        /// <summary>Creates an independent video VOD playlist after successful completion. Metadata remains usable after disposal.</summary>
        public string CreateVodPlaylist(Func<int, string> segmentUri)
        {
            if (!completed)
            {
                throw new InvalidOperationException("Complete the HLS presentation before creating its playlist.");
            }
            return HlsVodPlaylist.Create(Segments, static segment => segment.Duration, segmentUri,
                options.MaximumSegmentUriCharacters, independent: true);
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
                    transport = MpegTsWriter.Create(next, [stream], allocator, options.Transport);
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

        private void AddSegment(long end) => segments.Add(new(segments.Count, new(segmentStart, stream.TimeBase),
            new(checked(end - segmentStart), stream.TimeBase), checked(transport!.BytesWritten - segmentBytesStart)));

        private long ExactTicks(MediaTimestamp timestamp)
        {
            MediaTimestamp scaled = timestamp.Rescale(stream.TimeBase, TimestampRounding.TowardZero);
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
