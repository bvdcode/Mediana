// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Streaming.Hls
{
    internal class HlsSegmentOutput : IAsyncDisposable
    {
        private readonly IReadOnlyList<EncodedStreamInfo> streams;
        private readonly TimeBase clock;
        private readonly Func<int, MpegTsSegmentState?, CancellationToken, ValueTask<Stream>> openSegment;
        private readonly bool captureState;
        private readonly MediaBufferAllocator allocator;
        private readonly MpegTsWriteOptions options;
        private readonly int? maximumSegments;
        private readonly List<HlsMediaSegment> segments = [];
        private MpegTsWriter? transport;
        private Stream? destination;
        private long segmentBytesStart;
        private bool faulted;
        private bool disposed;
        private int busy;

        public HlsSegmentOutput(IReadOnlyList<EncodedStreamInfo> streams, TimeBase clock,
            Func<int, MpegTsSegmentState?, CancellationToken, ValueTask<Stream>> openSegment, MediaBufferAllocator allocator,
            MpegTsWriteOptions options, int? maximumSegments, bool captureState = true)
        {
            this.streams = streams;
            this.clock = clock;
            this.openSegment = openSegment;
            this.captureState = captureState;
            this.allocator = allocator;
            this.options = options;
            this.maximumSegments = maximumSegments;
            Segments = segments.AsReadOnly();
        }

        public IReadOnlyList<HlsMediaSegment> Segments { get; }
        public long BytesWritten => transport?.BytesWritten ?? 0;
        public bool HasOutput => transport is not null;
        public bool IsComplete { get; private set; }
        public long SegmentStart { get; set; }

        public async ValueTask BeginAsync(long start, CancellationToken token)
        {
            int number = transport is null ? 0 : checked(segments.Count + 1);
            if (maximumSegments is int limit && number >= limit)
            {
                throw new NotSupportedException("The HLS presentation exceeds the configured segment count limit.");
            }
            MpegTsSegmentState? state = captureState ? transport?.CaptureSegmentState() : null;
            Stream next = await openSegment(number, state, token).ConfigureAwait(false)
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
                    transport = MpegTsWriter.Create(next, streams, allocator, options);
                }
                else
                {
                    await transport.BeginSegmentAsync(next, token).ConfigureAwait(false);
                    AddSegment(start);
                }
                Stream? previous = destination;
                destination = next;
                selected = true;
                SegmentStart = start;
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

        public ValueTask WritePacketAsync(MediaPacket packet, CancellationToken token) => transport!.WritePacketAsync(packet, token);

        public async ValueTask CompleteAsync(long end, CancellationToken token)
        {
            await transport!.CompleteAsync(token).ConfigureAwait(false);
            AddSegment(end);
            await destination!.DisposeAsync().ConfigureAwait(false);
            destination = null;
            IsComplete = true;
        }

        public void EnsureComplete()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException("Complete the HLS presentation before reading its playlist metadata.");
            }
        }

        public void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

        public void EnsureWritable()
        {
            EnsureNotDisposed();
            if (IsComplete || faulted)
            {
                throw new InvalidOperationException("The HLS writer is complete or faulted.");
            }
        }

        public void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent HLS writer operations are not supported.");
            }
        }

        public void FinishOperation(bool success)
        {
            faulted |= !success;
            Volatile.Write(ref busy, 0);
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

        private void AddSegment(long end) => segments.Add(new(segments.Count, new(SegmentStart, clock),
            new(checked(end - SegmentStart), clock), checked(transport!.BytesWritten - segmentBytesStart)));
    }
}
