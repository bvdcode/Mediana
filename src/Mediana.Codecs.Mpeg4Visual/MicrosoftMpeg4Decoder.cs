// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Mpeg4Visual
{
    internal class MicrosoftMpeg4Decoder : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly EncodedStreamInfo stream;
        private readonly MicrosoftMpeg4PictureDecoder pictures;
        private readonly VideoFormat format;
        private VideoFrame? output;
        private bool completed;
        private bool disposed;
        private bool faulted;
        private int busy;

        internal MicrosoftMpeg4Decoder(EncodedStreamInfo stream, MediaBufferAllocator allocator,
            MicrosoftMpeg4Configuration configuration, bool wmv2)
        {
            this.stream = stream;
            pictures = new(configuration, allocator, wmv2);
            VideoColorInfo color = new(Range: ColorRange.Limited, ChromaLocation: ChromaLocation.Left);
            SampleAspectRatio aspect = new(1, 1);
            format = new(configuration.Width, configuration.Height, VideoPixelFormat.Yuv420P8, color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (packet.StreamIndex != stream.StreamIndex)
            {
                throw new ArgumentException("The packet belongs to a different Microsoft MPEG-4 stream.", nameof(packet));
            }
            Enter();
            try
            {
                EnsureUsable();
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("Microsoft MPEG-4 input is complete.");
                }
                if (output is not null)
                {
                    return CodecInputStatus.OutputRequired;
                }

                bool decoded = false;
                try
                {
                    using MediaBuffer pixels = pictures.Decode(packet.Data.Span, cancellationToken);
                    output = new(format, pixels, pictures.Planes, packet.PresentationTime ?? packet.DecodeTime, packet.Duration);
                    decoded = true;
                    return CodecInputStatus.Accepted;
                }
                finally
                {
                    if (!decoded)
                    {
                        faulted = true;
                        pictures.Reset();
                    }
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
                pictures.Reset();
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
            pictures.Reset();
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The Microsoft MPEG-4 decoder requires reset after decoding fails.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent Microsoft MPEG-4 decoder operations are not supported.");
            }
        }
    }
}
