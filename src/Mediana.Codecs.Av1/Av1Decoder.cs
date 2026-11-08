// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Av1
{
    internal class Av1Decoder(EncodedStreamInfo stream, MediaBufferAllocator allocator, Av1Configuration configuration) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private Av1PictureDecoder? pictures;
        private Av1FrameHeader? layoutHeader;
        private VideoFormat? format;
        private PlaneLayout[]? planes;
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
                if (completed)
                {
                    throw new InvalidOperationException("AV1 input is complete.");
                }
                if (output is not null)
                {
                    success = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (packet.StreamIndex != stream.StreamIndex)
                {
                    throw new ArgumentException("The packet belongs to a different AV1 stream.", nameof(packet));
                }
                pictures ??= new(allocator, configuration);
                using Av1Picture picture = pictures.Decode(packet.Data.Span, cancellationToken);
                Av1FrameHeader header = picture.Header;
                if (layoutHeader is not Av1FrameHeader previous || previous.Geometry != header.Geometry || previous.RenderWidth != header.RenderWidth || previous.RenderHeight != header.RenderHeight)
                {
                    format = Av1VideoFormat.Create(header, stream);
                    planes = Av1VideoFormat.Layout(header.Geometry);
                    layoutHeader = header;
                }
                output = new(format!, picture.Pixels, planes!, packet.PresentationTime, packet.Duration);
                success = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!success)
                {
                    faulted = true;
                    ReleasePictures();
                }
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
            finally
            {
                Volatile.Write(ref busy, 0);
            }
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
                ReleasePictures();
                completed = false;
                faulted = false;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Dispose()
        {
            Enter();
            try { disposed = true; ReleasePictures(); }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void ReleasePictures()
        {
            output?.Dispose();
            output = null;
            pictures = null;
            layoutHeader = null;
            format = null;
            planes = null;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The AV1 decoder requires reset or disposal after a failure.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent AV1 codec operations are not supported.");
            }
        }
    }
}
