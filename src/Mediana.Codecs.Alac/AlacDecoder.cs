// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Audio;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Alac
{
    internal class AlacDecoder(int streamIndex, AlacConfiguration configuration, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, AudioFrame>
    {
        private readonly AudioFormat format = new(configuration.SampleRate, AudioSampleFormat.Signed32,
            AudioStorageLayout.Interleaved, configuration.GetChannelOrder());
        private AudioFrame? output;

        private bool completed;
        private bool disposed;
        private bool faulted;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureUsable();
                ArgumentNullException.ThrowIfNull(packet);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("ALAC input is complete."); }
                if (output is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != streamIndex)
                { throw new ArgumentException("The packet belongs to a different ALAC stream.", nameof(packet)); }
                output = AlacPacketDecoder.Decode(packet, configuration, format, allocator, cancellationToken);
                success = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!success) { faulted = true; output?.Dispose(); output = null; }
                Volatile.Write(ref busy, 0);
            }
        }

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureUsable();
                cancellationToken.ThrowIfCancellationRequested();
                if (output is not null)
                {
                    AudioFrame frame = output;
                    output = null;
                    return CodecOutput<AudioFrame>.Available(frame);
                }
                return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void CompleteInput()
        {
            Enter();
            try { EnsureUsable(); completed = true; }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Reset()
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                output?.Dispose(); output = null;
                completed = faulted = false;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Dispose()
        {
            Enter();
            try { disposed = true; output?.Dispose(); output = null; }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The ALAC decoder requires reset or disposal after a failure."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent ALAC codec operations are not supported."); }
        }
    }
}
