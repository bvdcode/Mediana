// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Execution
{
    /// <summary>Submits each borrowed frame to multiple encoding pipelines in registration order.
    /// Each pipeline drains before the next input; the group uses no frame queues or worker threads.
    /// Pipelines, encoders and writers remain borrowed; completion drains codecs without finalizing writers.
    /// Failure or cancellation is terminal, including partial submission to the group.</summary>
    public class MediaEncodeGroup<TFrame> where TFrame : class, IDisposable
    {
        private readonly MediaEncodePipeline<TFrame>[] pipelines;
        private bool completed;
        private bool faulted;
        private int busy;

        public MediaEncodeGroup(IReadOnlyList<MediaEncodePipeline<TFrame>> pipelines)
        {
            ArgumentNullException.ThrowIfNull(pipelines);
            this.pipelines = pipelines.ToArray();
            if (this.pipelines.Length == 0 || this.pipelines.Any(pipeline => pipeline is null)
                || this.pipelines.Distinct().Count() != this.pipelines.Length)
            {
                throw new ArgumentException("Supply at least one distinct nonnull encoding pipeline.", nameof(pipelines));
            }
        }

        public long FramesSubmitted { get; private set; }

        public async ValueTask WriteAsync(TFrame frame, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                foreach (MediaEncodePipeline<TFrame> pipeline in pipelines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await pipeline.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                }
                FramesSubmitted = checked(FramesSubmitted + 1);
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask CompleteInputAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                foreach (MediaEncodePipeline<TFrame> pipeline in pipelines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await pipeline.CompleteInputAsync(cancellationToken).ConfigureAwait(false);
                }
                completed = true;
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        private void RequireActive()
        {
            if (faulted || completed)
            {
                throw new InvalidOperationException("The encoding group is no longer active.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent encoding group operations are not supported.");
            }
        }
    }
}
