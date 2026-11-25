// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;

namespace Mediana.Codecs.Pcm
{
    internal abstract class PcmPacketDecoder(int streamIndex, int maxSamplesPerFrame) : IMediaCodec<MediaPacket, AudioFrame>
    {
        private AudioFrame? pending;
        private bool completed;
        private bool disposed;
        private bool faulted;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            EnsureUsable();
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("Audio input is complete.");
                }
                if (pending is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty)
                {
                    throw new InvalidDataException("The audio packet is empty or belongs to a different stream.");
                }
                pending = Decode(input, maxSamplesPerFrame, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                succeeded = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!succeeded)
                {
                    Fault();
                }
            }
        }

        protected abstract AudioFrame Decode(MediaPacket packet, int maxSamples, CancellationToken cancellationToken);

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested)
            {
                Fault();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (pending is not null)
            {
                AudioFrame frame = pending;
                pending = null;
                return CodecOutput<AudioFrame>.Available(frame);
            }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            EnsureUsable();
            completed = true;
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose();
            pending = null;
            completed = false;
            faulted = false;
        }

        private void Fault()
        {
            pending?.Dispose();
            pending = null;
            faulted = true;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The audio decoder has failed and requires reset or disposal.");
            }
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Reset();
                disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
