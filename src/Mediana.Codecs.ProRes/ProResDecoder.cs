// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.ProRes
{
    internal class ProResDecoder(EncodedStreamInfo stream, MediaBufferAllocator allocator,
        ProResDecoderOptions options, Action<string>? warningHandler) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private VideoFrame? output;
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
                if (completed) { throw new InvalidOperationException("ProRes input is complete."); }
                if (output is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != stream.StreamIndex)
                { throw new ArgumentException("The packet belongs to a different ProRes stream.", nameof(packet)); }
                output = ProResPictureDecoder.Decode(packet, stream, allocator, options, warningHandler, cancellationToken);
                success = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!success) { faulted = true; output?.Dispose(); output = null; }
                Volatile.Write(ref busy, 0);
            }
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureUsable();
                cancellationToken.ThrowIfCancellationRequested();
                if (output is not null)
                {
                    VideoFrame frame = output;
                    output = null;
                    return CodecOutput<VideoFrame>.Available(frame);
                }
                return completed ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
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
            if (faulted) { throw new InvalidOperationException("The ProRes decoder requires reset or disposal after a failure."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent ProRes codec operations are not supported."); }
        }
    }
}
