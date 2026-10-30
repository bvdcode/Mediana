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
        public ContainerInfo Info => Reader.Info;
        public IReadOnlyList<EncodedStreamInfo> Streams => Reader.Streams;
        public long AllocatedBufferBytes => Allocator.AllocatedBytes;
        /// <summary>Picture declarations in container order. Null means the reader does not expose pictures;
        /// an empty list means no pictures were found in its supported inspected metadata.</summary>
        public IReadOnlyList<ContainerPictureInfo>? Pictures => (Reader as IContainerPictureReader)?.Pictures;

        /// <summary>Reads original picture/reference bytes without advancing packets or selecting a cover.
        /// Returned storage belongs to the caller and survives session disposal. Cancellation or a reader
        /// failure faults the session; an unsupported capability or invalid index leaves it usable.</summary>
        public async ValueTask<MediaBuffer> ReadPictureAsync(int pictureIndex, CancellationToken cancellationToken = default)
        {
            Enter();
            bool started = false;
            bool succeeded = false;
            try
            {
                RequireActive();
                if (Reader is not IContainerPictureReader pictures)
                {
                    throw new NotSupportedException("The container reader does not expose pictures.");
                }
                ArgumentOutOfRangeException.ThrowIfNegative(pictureIndex);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pictureIndex, pictures.Pictures.Count);
                started = true;
                cancellationToken.ThrowIfCancellationRequested();
                MediaBuffer picture = await pictures.ReadPictureAsync(pictureIndex, cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return picture;
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

        private void RequireActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The container session has failed and must be disposed.");
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
