// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;

namespace Mediana.Execution
{
    /// <summary>Encodes one stream without a packet queue. Borrows the encoder and frame callback;
    /// each callback result transfers ownership. Failure or cancellation is terminal. Calls must not overlap.</summary>
    public class MediaEncodeSource<TFrame> : IEncodedPacketSource where TFrame : class, IDisposable
    {
        private readonly Func<CancellationToken, ValueTask<TFrame?>> read;
        private readonly IMediaEncoder<TFrame> encoder;
        private bool inputCompleted;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public MediaEncodeSource(Func<CancellationToken, ValueTask<TFrame?>> read, IMediaEncoder<TFrame> encoder)
        {
            ArgumentNullException.ThrowIfNull(read);
            ArgumentNullException.ThrowIfNull(encoder);
            this.read = read;
            this.encoder = encoder;
        }

        public EncodedStreamInfo Stream => encoder.Stream;

        public async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The encoding source has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (ended)
                {
                    success = true;
                    return null;
                }
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CodecOutput<MediaPacket> output = encoder.Receive(cancellationToken);
                    switch (output.Status)
                    {
                        case CodecOutputStatus.Available:
                            success = true;
                            return output.Value;
                        case CodecOutputStatus.EndOfStream:
                            if (!inputCompleted)
                            {
                                throw new InvalidOperationException("The encoder ended before input completion.");
                            }
                            ended = true;
                            success = true;
                            return null;
                        case CodecOutputStatus.InputRequired:
                            if (inputCompleted)
                            {
                                throw new InvalidOperationException("The encoder requested input while draining.");
                            }
                            await SendFrameAsync(cancellationToken).ConfigureAwait(false);
                            break;
                        default:
                            throw new InvalidOperationException("The encoder returned an invalid output state.");
                    }
                }
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask SendFrameAsync(CancellationToken token)
        {
            using TFrame? frame = await read(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (frame is null)
            {
                encoder.CompleteInput();
                inputCompleted = true;
                return;
            }
            if (encoder.TrySend(frame, token) != CodecInputStatus.Accepted)
            {
                throw new InvalidOperationException("The encoder refused a frame after requesting input.");
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                GC.SuppressFinalize(this);
                return ValueTask.CompletedTask;
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
                throw new InvalidOperationException("Encoding source operations must not overlap.");
            }
        }
    }
}
