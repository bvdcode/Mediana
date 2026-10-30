// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana
{
    /// <summary>Forward-only encoded packets. Owns container state but borrows the source.
    /// Returned packets retain their storage until disposed, including after session disposal.</summary>
    public class ContainerSession : IContainerReader
    {
        private readonly IByteSource replay;
        private readonly long? maxPacketReads;
        private long packetReads;
        private bool disposed;
        private bool faulted;
        private int busy;

        internal ContainerSession(IContainerReader reader, IByteSource replay, MediaBufferAllocator allocator, long? maxPacketReads)
        {
            Reader = reader;
            this.replay = replay;
            Allocator = allocator;
            this.maxPacketReads = maxPacketReads;
        }

        internal IContainerReader Reader { get; }
        internal MediaBufferAllocator Allocator { get; }
        public IReadOnlyList<EncodedStreamInfo> Streams => Reader.Streams;
        public long AllocatedBufferBytes => Allocator.AllocatedBytes;

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The container session has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (maxPacketReads is long limit)
                {
                    if (packetReads == limit)
                    {
                        throw new InvalidOperationException($"The configured session limit of {limit} packet reads has been reached.");
                    }
                    packetReads++;
                }
                MediaPacket? packet = await Reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return packet;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                }
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
                        await Reader.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await replay.DisposeAsync().ConfigureAwait(false);
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
                throw new InvalidOperationException("Concurrent container session operations are not supported.");
            }
        }
    }
}
