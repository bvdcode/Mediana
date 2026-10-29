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
    public class MediaDecodePipeline<TFrame> : IDisposable where TFrame : class, IDisposable
    {
        private readonly IContainerReader reader;
        private readonly IMediaCodec<MediaPacket, TFrame> decoder;
        private readonly int streamIndex;
        private readonly long maxPacketReads;
        private long packetReads;
        private bool inputCompleted;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private bool decoderDisposed;
        private int busy;

        public MediaDecodePipeline(IContainerReader reader, IMediaCodec<MediaPacket, TFrame> decoder,
            int streamIndex, long maxPacketReads)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(decoder);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPacketReads);
            this.reader = reader;
            this.decoder = decoder;
            this.streamIndex = streamIndex;
            this.maxPacketReads = maxPacketReads;
        }

        /// <summary>Returns an owned frame or null at EOF. The limit counts all container reads, including EOF.</summary>
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
                if (packetReads == maxPacketReads)
                {
                    throw new InvalidOperationException("The container read limit has been reached.");
                }
                packetReads++;
                using MediaPacket? packet = await reader.ReadPacketAsync(cancellationToken).ConfigureAwait(false);
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
                decoder.Dispose();
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
