// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Execution;

namespace Mediana
{
    /// <summary>Owns container/decoder state but not the caller's source.
    /// Returned frames survive session disposal until their owners release them.</summary>
    internal partial class MediaDecodeSession<TFrame> : IAsyncDisposable where TFrame : class, IDisposable
    {
        private readonly ContainerSession container;
        private readonly MediaDecodePipeline<TFrame> pipeline;
        private readonly long? maxFrameReads;
        private long frameReads;
        private bool disposed;
        private bool faulted;
        private int busy;

        internal MediaDecodeSession(ContainerSession container, MediaDecodePipeline<TFrame> pipeline,
            long? maxFrameReads, EncodedStreamInfo stream)
        {
            this.container = container;
            this.pipeline = pipeline;
            this.maxFrameReads = maxFrameReads;
            Stream = stream;
        }

        public long AllocatedBufferBytes => container.AllocatedBufferBytes;
        /// <summary>Immutable selected stream metadata, including container display information.</summary>
        public EncodedStreamInfo Stream { get; }

        public async ValueTask<TFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask<TFrame?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The session has failed and must be disposed.");
            }
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (maxFrameReads is long limit)
                {
                    if (frameReads == limit)
                    {
                        throw new InvalidOperationException($"The configured session limit of {limit} frame reads has been reached.");
                    }
                    frameReads++;
                }
                TFrame? frame = await pipeline.ReadAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return frame;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                    pipeline.Dispose();
                }
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
                        pipeline.Dispose();
                    }
                    finally
                    {
                        await container.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        public void Exit() => Volatile.Write(ref busy, 0);

        public void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent session operations are not supported.");
            }
        }
    }
}
