// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Selects whole decoded frames whose presentation timestamps fall in [start, end).
    /// A null end reads through EOF. Input must have known, strictly increasing presentation timestamps.
    /// Timestamps, durations and pixels are unchanged; a selected frame may extend past end.
    /// The callback transfers frame ownership; its source remains borrowed. Selected frames are returned
    /// directly without retaining or copying their storage. Other frames are disposed.
    /// Reads at most one frame at or beyond end. Seeking belongs to the caller.
    /// Failure or cancellation makes the reader terminal.</summary>
    public class VideoTrimmer : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<VideoFrame?>> readFrame;
        private readonly MediaTimestamp start;
        private readonly MediaTimestamp? end;
        private MediaTimestamp? previous;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public VideoTrimmer(Func<CancellationToken, ValueTask<VideoFrame?>> readFrame,
            MediaTimestamp start, MediaTimestamp? end = null)
        {
            ArgumentNullException.ThrowIfNull(readFrame);
            start.TimeBase.Validate();
            end?.TimeBase.Validate();
            if (end is MediaTimestamp limit)
            {
                if (limit.CompareTo(start) < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(end), "The video window end must not precede its start.");
                }
                ended = limit == start;
            }
            this.readFrame = readFrame;
            this.start = start;
            this.end = end;
        }

        public async ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The video trimmer has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                VideoFrame? result = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return result;
            }
            finally
            {
                faulted |= !succeeded;
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<VideoFrame?> ReadCoreAsync(CancellationToken token)
        {
            while (!ended)
            {
                token.ThrowIfCancellationRequested();
                VideoFrame? frame = await readFrame(token).ConfigureAwait(false);
                bool selected = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (frame is null)
                    {
                        ended = true;
                        return null;
                    }
                    MediaTimestamp timestamp = frame.Timestamp
                        ?? throw new NotSupportedException("Video trimming requires presentation timestamps.");
                    if (previous is MediaTimestamp last && timestamp.CompareTo(last) <= 0)
                    {
                        throw new InvalidDataException("Video trimming requires strictly increasing presentation timestamps.");
                    }
                    previous = timestamp;
                    if (end is MediaTimestamp limit && timestamp.CompareTo(limit) >= 0)
                    {
                        ended = true;
                        return null;
                    }
                    if (timestamp.CompareTo(start) >= 0)
                    {
                        selected = true;
                        return frame;
                    }
                }
                finally
                {
                    if (!selected)
                    {
                        frame?.Dispose();
                    }
                }
            }
            return null;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Video trimming operations must not overlap.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                GC.SuppressFinalize(this);
                return ValueTask.CompletedTask;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
