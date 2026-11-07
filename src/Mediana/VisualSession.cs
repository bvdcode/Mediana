// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana
{
    /// <summary>Sequential visual decoding for images, document pages, animations and container video.
    /// Borrows the byte source and allocator. Every returned frame transfers independent ownership.
    /// Calls must not overlap; a failed read requires disposal.</summary>
    public abstract class VisualSession : IAsyncDisposable
    {
        private int busy;
        private bool disposed;
        private bool faulted;

        public abstract bool IsImage { get; }
        public abstract VideoDisplayMatrix? DisplayMatrix { get; }
        public abstract MediaDuration? CodedDuration { get; }

        public async ValueTask<VideoFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("A failed visual session must be disposed."); }
                cancellationToken.ThrowIfCancellationRequested();
                VideoFrame? frame = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return frame;
            }
            finally
            {
                if (!succeeded) { faulted = true; }
                Volatile.Write(ref busy, 0);
            }
        }

        protected abstract ValueTask<VideoFrame?> ReadCoreAsync(CancellationToken cancellationToken);
        protected abstract ValueTask DisposeCoreAsync();

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (disposed) { return; }
                disposed = true;
                await DisposeCoreAsync().ConfigureAwait(false);
            }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Visual session operations must not overlap."); }
        }
    }
}
