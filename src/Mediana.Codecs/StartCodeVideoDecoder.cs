// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Video;

namespace Mediana.Codecs
{
    internal class StartCodeVideoDecoder(StartCodePictureReader packets, IMediaCodec<MediaPacket, VideoFrame> decoder)
        : IMediaCodec<MediaPacket, VideoFrame>
    {
        private bool needsInput = true;
        private bool complete;
        private bool draining;
        private bool faulted;
        private bool disposed;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            Enter();
            bool succeeded = false;
            try
            {
                EnsureReadable();
                cancellationToken.ThrowIfCancellationRequested();
                if (complete)
                {
                    throw new InvalidOperationException("Video input has been completed.");
                }
                succeeded = true;
                if (!needsInput)
                {
                    return CodecInputStatus.OutputRequired;
                }
                succeeded = false;
                packets.Append(input);
                needsInput = false;
                succeeded = true;
                return CodecInputStatus.Accepted;
            }
            finally { Finish(succeeded); }
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureReadable();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CodecOutput<VideoFrame> output = decoder.Receive(cancellationToken);
                    if (output.Status != CodecOutputStatus.InputRequired)
                    {
                        succeeded = true;
                        return output;
                    }
                    if (packets.TryRead(complete, out MediaPacket? packet, cancellationToken))
                    {
                        using (packet)
                        {
                            if (decoder.TrySend(packet!, cancellationToken) != CodecInputStatus.Accepted)
                            {
                                throw new InvalidOperationException("The video decoder did not accept a completed picture.");
                            }
                        }
                        continue;
                    }
                    if (!complete)
                    {
                        needsInput = true;
                        succeeded = true;
                        return CodecOutput<VideoFrame>.InputRequired;
                    }
                    if (draining)
                    {
                        throw new InvalidOperationException("The video decoder requested input while draining.");
                    }
                    decoder.CompleteInput();
                    draining = true;
                }
            }
            finally { Finish(succeeded); }
        }

        public void CompleteInput()
        {
            Enter();
            try
            {
                EnsureReadable();
                complete = true;
            }
            finally { Exit(); }
        }

        public void Reset()
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                packets.Dispose();
                decoder.Reset();
                needsInput = true;
                complete = draining = faulted = false;
                succeeded = true;
            }
            finally { Finish(succeeded); }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                if (!disposed)
                {
                    disposed = true;
                    try { decoder.Dispose(); }
                    finally { packets.Dispose(); }
                }
            }
            finally { Exit(); }
        }

        private void EnsureReadable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("Reset is required after a video packetization or decoding error.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent video decoder operations are not supported.");
            }
        }

        private void Finish(bool succeeded)
        {
            try
            {
                if (!succeeded)
                {
                    faulted = true;
                    packets.Dispose();
                }
            }
            finally { Exit(); }
        }

        private void Exit() => Volatile.Write(ref busy, 0);
    }
}
