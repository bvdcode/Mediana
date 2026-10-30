// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Containers;
using Mediana.Core;

namespace Mediana.Execution
{
    /// <summary>Single-consumer encoding pump. Borrows encoder, writer and submitted frames.
    /// Drains all available packets before accepting more input. Failure is terminal; the owner disposes borrowed components.</summary>
    public class MediaEncodePipeline<TFrame>
        where TFrame : class, IDisposable
    {
        private readonly IMediaCodec<TFrame, MediaPacket> encoder;
        private readonly IContainerWriter writer;

        public MediaEncodePipeline(IMediaCodec<TFrame, MediaPacket> encoder, IContainerWriter writer)
        {
            ArgumentNullException.ThrowIfNull(encoder);
            ArgumentNullException.ThrowIfNull(writer);
            this.encoder = encoder;
            this.writer = writer;
        }

        private bool completed;
        private bool faulted;
        private int busy;
        public long FramesSubmitted { get; private set; }
        public long PacketsWritten { get; private set; }
        public long PayloadBytes { get; private set; }

        public async ValueTask WriteAsync(TFrame frame, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(frame);
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                await DrainAsync(false, cancellationToken).ConfigureAwait(false);
                if (encoder.TrySend(frame, cancellationToken) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The encoder refused input after requesting it.");
                }
                FramesSubmitted = checked(FramesSubmitted + 1);
                await DrainAsync(false, cancellationToken).ConfigureAwait(false);
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Drains delayed packets after ending input. Does not complete the borrowed writer, which may serve other encoders.</summary>
        public async ValueTask CompleteInputAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                RequireActive();
                cancellationToken.ThrowIfCancellationRequested();
                await DrainAsync(false, cancellationToken).ConfigureAwait(false);
                encoder.CompleteInput();
                await DrainAsync(true, cancellationToken).ConfigureAwait(false);
                completed = true;
                success = true;
            }
            finally
            {
                faulted |= !success;
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask DrainAsync(bool draining, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                CodecOutput<MediaPacket> output = encoder.Receive(token);
                switch (output.Status)
                {
                    case CodecOutputStatus.Available:
                        using (MediaPacket packet = output.Value)
                        {
                            await writer.WritePacketAsync(packet, token).ConfigureAwait(false);
                            PacketsWritten = checked(PacketsWritten + 1);
                            PayloadBytes = checked(PayloadBytes + packet.Data.Length);
                        }
                        break;
                    case CodecOutputStatus.InputRequired when !draining:
                    case CodecOutputStatus.EndOfStream when draining:
                        return;
                    default:
                        throw new InvalidOperationException("The encoder returned an invalid state for its input lifetime.");
                }
            }
        }

        private void RequireActive()
        {
            if (faulted || completed)
            {
                throw new InvalidOperationException("The encoding pipeline is no longer active.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent encoding operations are not supported.");
            }
        }
    }
}
