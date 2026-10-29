// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Sample
{
    internal class RgbaSampleDecoder(MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private VideoFrame? pending;
        private bool completed;
        private bool disposed;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed)
            {
                throw new InvalidOperationException("Input is complete.");
            }
            if (pending is not null)
            {
                return CodecInputStatus.OutputRequired;
            }
            if (input.Data.Length != 16)
            {
                throw new InvalidDataException("The sample requires one 2x2 RGBA frame.");
            }
            using WritableMediaBuffer writable = allocator.Allocate(input.Data.Length);
            input.Data.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32);
            pending = new VideoFrame(format, buffer, format.CreateTightLayout(), input.PresentationTime);
            return CodecInputStatus.Accepted;
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (pending is not null)
            {
                VideoFrame frame = pending;
                pending = null;
                return CodecOutput<VideoFrame>.Available(frame);
            }
            if (completed)
            {
                return CodecOutput<VideoFrame>.EndOfStream;
            }
            return CodecOutput<VideoFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            completed = true;
        }
        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose();
            pending = null;
            completed = false;
        }
        public void Dispose()
        {
            if (!disposed)
            {
                Reset();
                disposed = true;
            }
        }
    }
}
