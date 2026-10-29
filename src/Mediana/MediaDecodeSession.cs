// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana
{
    /// <summary>Forward-only decoding. Owns container/decoder state but not the caller's source.
    /// Returned frames survive session disposal until their owners release them.</summary>
    internal class MediaDecodeSession<TFrame> : IAsyncDisposable where TFrame : class, IDisposable
    {
        private readonly IContainerReader reader;
        private readonly IByteSource replay;
        private readonly MediaDecodePipeline<TFrame> pipeline;
        private readonly MediaBufferAllocator allocator;
        private readonly long maxFrameReads;
        private long frameReads;
        private bool disposed;
        private bool faulted;
        private int busy;

        internal MediaDecodeSession(IContainerReader reader, IByteSource replay, MediaDecodePipeline<TFrame> pipeline,
            MediaBufferAllocator allocator, long maxFrameReads, EncodedStreamInfo stream)
        {
            this.reader = reader;
            this.replay = replay;
            this.pipeline = pipeline;
            this.allocator = allocator;
            this.maxFrameReads = maxFrameReads;
            Stream = stream;
        }

        public long AllocatedBufferBytes => allocator.AllocatedBytes;
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
                if (frameReads == maxFrameReads)
                {
                    throw new InvalidOperationException("The session frame read limit has been reached.");
                }
                frameReads++;
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
                        try
                        {
                            await reader.DisposeAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            await replay.DisposeAsync().ConfigureAwait(false);
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
