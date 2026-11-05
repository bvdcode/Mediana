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
    public partial class ContainerSession : IContainerReader
    {
        private readonly IByteSource? replay;
        internal MediaOpenOptions Options { get; }
        private long packetReads;
        private bool packetsStarted;
        private bool disposed;
        private bool faulted;
        private int busy;

        internal ContainerSession(IContainerReader reader, IByteSource? replay, MediaBufferAllocator allocator, MediaOpenOptions options)
        {
            Reader = reader;
            this.replay = replay;
            Allocator = allocator;
            Options = options;
        }

        internal IContainerReader Reader { get; }
        internal MediaBufferAllocator Allocator { get; }
        public ContainerInfo Info => Reader.Info;
        public IReadOnlyList<EncodedStreamInfo> Streams => Reader.Streams;
        public long AllocatedBufferBytes => Allocator.AllocatedBytes;
        internal T Transfer<T>(Func<ContainerSession, T> create)
        {
            Enter();
            try
            {
                RequireActive();
                if (packetsStarted)
                {
                    throw new InvalidOperationException("Decode-session transfer requires a container whose packets have not been read.");
                }
                ContainerSession owner = new(Reader, replay, Allocator, Options);
                T result = create(owner);
                disposed = true;
                return result;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private async ValueTask<T> ReadDataAsync<T>(int dataIndex, int? count,
            Func<int, CancellationToken, ValueTask<T>>? read, string capability, CancellationToken token)
        {
            Enter();
            bool started = false;
            bool succeeded = false;
            try
            {
                RequireActive();
                if (read is null || count is null)
                {
                    throw new NotSupportedException($"The container reader does not expose {capability}.");
                }
                ArgumentOutOfRangeException.ThrowIfNegative(dataIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dataIndex, count.Value);
                started = true;
                token.ThrowIfCancellationRequested();
                T data = await read(dataIndex, token).ConfigureAwait(false);
                succeeded = true;
                return data;
            }
            finally
            {
                if (started && !succeeded)
                {
                    faulted = true;
                }
                Volatile.Write(ref busy, 0);
            }
        }

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (Options.MaxPacketReads is long limit)
                {
                    if (packetReads == limit)
                    {
                        throw new InvalidOperationException($"The configured session limit of {limit} packet reads has been reached.");
                    }
                    packetReads++;
                }
                packetsStarted = true;
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
                        if (replay is not null) { await replay.DisposeAsync().ConfigureAwait(false); }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        private void RequireActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The container session has failed and must be disposed.");
            }
        }

        internal void ValidateActive()
        {
            Enter();
            try
            {
                RequireActive();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
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
