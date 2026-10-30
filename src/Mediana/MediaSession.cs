// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Execution;

namespace Mediana
{
    /// <summary>Decodes selected audio/video streams from one container and one borrowed source.
    /// A single consumer receives owned frames; no packets are read between calls. Frames survive session disposal.</summary>
    public class MediaSession : IAsyncDisposable
    {
        private readonly ContainerSession container;
        private readonly MediaDecodeGroup group;
        private int busy;
        private bool disposed;

        internal MediaSession(ContainerSession container, MediaDecodeGroup group, EncodedStreamInfo[] streams)
        {
            this.container = container;
            this.group = group;
            Streams = Array.AsReadOnly(streams);
        }

        public IReadOnlyList<EncodedStreamInfo> Streams { get; }
        public long AllocatedBufferBytes => container.AllocatedBufferBytes;

        public async ValueTask<DecodedMediaFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return await group.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
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
                        group.Dispose();
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

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent media session operations are not supported.");
            }
        }
    }
}
