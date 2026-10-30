// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;

namespace Mediana.Execution
{
    /// <summary>Pull-driven decoding with no unbounded packet queue.
    /// Owns the decoder after construction; borrows the container reader.
    /// Cancellation or failure makes the pipeline terminal. Calls and disposal must not overlap.</summary>
    public partial class MediaDecodePipeline<TFrame> : IDisposable where TFrame : class, IDisposable
    {
        private readonly IContainerReader reader;
        private readonly IMediaCodec<MediaPacket, TFrame> decoder;
        private readonly int streamIndex;
        private readonly long? maxPacketReads;
        private readonly IRandomAccessInspector? randomAccessInspector;
        private MediaPacket? pendingPacket;
        private long packetReads;
        private bool inputCompleted;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private bool decoderDisposed;
        private int busy;

        public MediaDecodePipeline(IContainerReader reader, IMediaCodec<MediaPacket, TFrame> decoder,
            int streamIndex, long? maxPacketReads = null, IRandomAccessInspector? randomAccessInspector = null)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(decoder);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            if (maxPacketReads is long limit)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
            }
            this.reader = reader is IIndexedContainerReader indexed && randomAccessInspector is not null
                ? new IndexedPacketReader(indexed, streamIndex) : reader;
            this.decoder = decoder;
            this.streamIndex = streamIndex;
            this.maxPacketReads = maxPacketReads;
            this.randomAccessInspector = randomAccessInspector;
        }

        /// <summary>Returns an owned frame or null at EOF. An optional limit counts all container reads, including EOF.</summary>
        public async ValueTask<TFrame?> ReadAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The decoding pipeline has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (ended)
                {
                    succeeded = true;
                    return null;
                }
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CodecOutput<TFrame> output = decoder.Receive(cancellationToken);
                    switch (output.Status)
                    {
                        case CodecOutputStatus.Available:
                            succeeded = true;
                            return output.Value;
                        case CodecOutputStatus.EndOfStream:
                            if (!inputCompleted)
                            {
                                throw new InvalidOperationException("The decoder ended before its input was completed.");
                            }
                            ended = true;
                            succeeded = true;
                            return null;
                        case CodecOutputStatus.InputRequired:
                            if (inputCompleted)
                            {
                                throw new InvalidOperationException("The decoder requested input while draining.");
                            }
                            await SendNextPacketAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        default:
                            throw new InvalidOperationException("The decoder returned an invalid state.");
                    }
                }
            }
            finally
            {
                try
                {
                    if (!succeeded)
                    {
                        faulted = true;
                        DisposeDecoder();
                    }
                }
                finally
                {
                    Volatile.Write(ref busy, 0);
                }
            }
        }

        private async ValueTask SendNextPacketAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using MediaPacket? packet = await ReadNextPacketAsync(cancellationToken).ConfigureAwait(false);
                if (packet is null)
                {
                    decoder.CompleteInput();
                    inputCompleted = true;
                    return;
                }
                if (packet.StreamIndex != streamIndex)
                {
                    continue;
                }
                switch (decoder.TrySend(packet, cancellationToken))
                {
                    case CodecInputStatus.Accepted:
                        return;
                    case CodecInputStatus.OutputRequired:
                        throw new InvalidOperationException("The decoder requires both input and output without making progress.");
                    default:
                        throw new InvalidOperationException("The decoder returned an invalid input state.");
                }
            }
        }

        private async ValueTask<MediaPacket?> ReadNextPacketAsync(CancellationToken cancellationToken)
        {
            if (pendingPacket is MediaPacket packet)
            {
                pendingPacket = null;
                return packet;
            }
            CountPacketRead();
            return await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
        }

        private void CountPacketRead()
        {
            if (maxPacketReads is long limit)
            {
                if (packetReads == limit)
                {
                    throw new InvalidOperationException($"The configured container limit of {limit} packet reads has been reached.");
                }
                packetReads++;
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    DisposeDecoder();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }

        private void DisposeDecoder()
        {
            if (!decoderDisposed)
            {
                decoderDisposed = true;
                try
                {
                    decoder.Dispose();
                }
                finally
                {
                    pendingPacket?.Dispose();
                    pendingPacket = null;
                }
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent pipeline operations are not supported.");
            }
        }
    }
}
