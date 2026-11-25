// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Dv
{
    internal class DvDecoder(EncodedStreamInfo stream, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private VideoFrame? output;
        private bool completed;
        private bool faulted;
        private bool disposed;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (packet.StreamIndex != stream.StreamIndex)
            {
                throw new ArgumentException("The packet belongs to a different DV stream.", nameof(packet));
            }
            Enter();
            try
            {
                EnsureUsable();
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("DV input is complete.");
                }
                if (output is not null)
                {
                    return CodecInputStatus.OutputRequired;
                }
                bool succeeded = false;
                try
                {
                    output = DvPictureDecoder.Decode(packet, stream, allocator, cancellationToken);
                    succeeded = true;
                    return CodecInputStatus.Accepted;
                }
                finally
                {
                    faulted = !succeeded;
                }
            }
            finally
            {
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
                if (output is VideoFrame frame)
                {
                    output = null;
                    return CodecOutput<VideoFrame>.Available(frame);
                }
                return completed ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public void CompleteInput()
        {
            Enter();
            try
            {
                EnsureUsable();
                completed = true;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public void Reset()
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                Release();
                completed = faulted = false;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                disposed = true;
                Release();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private void Release()
        {
            output?.Dispose();
            output = null;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The DV decoder requires reset after a decoding failure.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent DV decoder operations are not supported.");
            }
        }
    }
}
