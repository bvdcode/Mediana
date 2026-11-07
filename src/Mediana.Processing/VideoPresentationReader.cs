// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Selects frames starting inside an optional presentation interval and clips overlapping
    /// display durations at the next frame or selected end. Short declared durations preserve gaps.
    /// Borrows the callback; owns at most one lookahead frame. Returned pixels retain their original storage.
    /// Requires ordered known timestamps and positive durations; no duration or pixels are synthesized.</summary>
    public class VideoPresentationReader : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<VideoFrame?>> read;
        private readonly MediaTimeRange? selection;
        private readonly VideoIntervalAlignment alignment;
        private MediaTimestamp? previousEnd;
        private VideoFrame? pending;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public VideoPresentationReader(Func<CancellationToken, ValueTask<VideoFrame?>> read, MediaTimeRange? selection = null,
            VideoIntervalAlignment alignment = VideoIntervalAlignment.ClipDisplayDuration)
        {
            ArgumentNullException.ThrowIfNull(read);
            if (!Enum.IsDefined(alignment)) { throw new ArgumentOutOfRangeException(nameof(alignment)); }
            if (selection is MediaTimeRange range) { range.Start.TimeBase.Validate(); range.End.TimeBase.Validate(); }
            this.read = read;
            this.selection = selection;
            this.alignment = alignment;
        }

        public async ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("The presentation reader has failed."); }
                cancellationToken.ThrowIfCancellationRequested();
                VideoFrame? result = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                success = true;
                return result;
            }
            finally
            {
                faulted |= !success;
                if (!success) { pending?.Dispose(); pending = null; }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<VideoFrame?> ReadCoreAsync(CancellationToken token)
        {
            if (ended || selection is MediaTimeRange empty && empty.Duration.Value == 0) { return null; }
            pending ??= await read(token).ConfigureAwait(false);
            while (pending is not null)
            {
                using VideoFrame current = pending;
                pending = null;
                token.ThrowIfCancellationRequested();
                MediaTimestamp start = current.Timestamp ?? throw new NotSupportedException("Video presentation requires known timestamps.");
                if (selection is MediaTimeRange limit && start.CompareTo(limit.End) >= 0)
                { ValidateCompletion(); ended = true; return null; }
                MediaDuration duration = current.Duration ?? throw new NotSupportedException("Video presentation requires known durations.");
                if (duration.Value == 0) { throw new InvalidDataException("Video presentation durations must be positive."); }
                TimeBase clock = TimeBase.Common(start.ReduceClock().TimeBase, new MediaTimestamp(duration.Value, duration.TimeBase).ReduceClock().TimeBase);
                MediaTimestamp end = new(checked(start.Rescale(clock, TimestampRounding.TowardZero).Value +
                    new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(clock, TimestampRounding.TowardZero).Value), clock);
                pending = await read(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (pending is VideoFrame next)
                {
                    MediaTimestamp nextTime = next.Timestamp ?? throw new NotSupportedException("Video presentation requires known timestamps.");
                    if (nextTime.CompareTo(start) <= 0) { throw new InvalidDataException("Video presentation requires strictly increasing timestamps."); }
                    if (nextTime.CompareTo(end) < 0) { end = nextTime; }
                }
                else { ended = true; }
                if (selection is MediaTimeRange range)
                {
                    if (start.CompareTo(range.Start) < 0) { continue; }
                    switch (alignment)
                    {
                        case VideoIntervalAlignment.ClipDisplayDuration:
                            if (range.End.CompareTo(end) < 0) { end = range.End; }
                            break;
                        case VideoIntervalAlignment.CompleteFrames:
                            if (start != (previousEnd ?? range.Start))
                            { throw new InvalidDataException("The selected video interval must begin on a frame boundary and contain contiguous frames."); }
                            if (end.CompareTo(range.End) > 0)
                            { throw new NotSupportedException("The selected interval must end on a complete video frame boundary."); }
                            if (ended && end != range.End)
                            { throw new InvalidDataException("The source ended before the complete video interval."); }
                            previousEnd = end;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(alignment));
                    }
                }
                MediaTimeRange interval = new(start.ReduceClock(), end.ReduceClock());
                return current.WithTiming(start, interval.Duration);
            }
            ValidateCompletion();
            ended = true;
            return null;
        }

        private void ValidateCompletion()
        {
            if (alignment == VideoIntervalAlignment.CompleteFrames && selection is MediaTimeRange range && previousEnd != range.End)
            { throw new InvalidDataException("The source does not fill the complete video interval."); }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; pending?.Dispose(); pending = null; return ValueTask.CompletedTask; }
            finally { Volatile.Write(ref busy, 0); GC.SuppressFinalize(this); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Video presentation operations must not overlap."); }
        }
    }
}
