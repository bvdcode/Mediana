// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Vp9
{
    internal class Vp9Decoder(EncodedStreamInfo stream, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private Vp9PictureDecoder? pictures;
        private VideoFormat? format;
        private Vp9PictureGeometry? geometry;
        private VideoFrame? output;
        private bool completed, disposed, faulted;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter(); bool success = false;
            try
            {
                EnsureUsable(); ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("VP9 input is complete."); }
                if (output is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != stream.StreamIndex) { throw new ArgumentException("The packet belongs to a different VP9 stream.", nameof(packet)); }
                Span<Range> ranges = stackalloc Range[8]; int count = Vp9Superframe.Parse(packet.Data.Span, ranges);
                pictures ??= new(allocator);
                for (int i = 0; i < count; i++)
                {
                    (Vp9FrameHeader header, Vp9Picture picture) = pictures.Decode(packet.Data[ranges[i]], cancellationToken);
                    using (picture)
                    {
                        if (!header.ShowFrame) { continue; }
                        if (output is not null) { throw new NotSupportedException("A VP9 access unit must contain at most one visible picture with its presentation timestamp."); }
                        if (geometry != picture.Geometry)
                        { format = Vp9VideoFormat.Create(picture.Geometry, stream); geometry = picture.Geometry; }
                        output = new(format!, picture.Pixels, Vp9VideoFormat.Layout(picture), packet.PresentationTime, packet.Duration);
                    }
                }
                success = true; return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!success) { faulted = true; ReleasePictures(); }
                Volatile.Write(ref busy, 0);
            }
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureUsable(); cancellationToken.ThrowIfCancellationRequested();
                if (output is not null) { VideoFrame frame = output; output = null; return CodecOutput<VideoFrame>.Available(frame); }
                return completed ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void CompleteInput()
        {
            Enter(); try { EnsureUsable(); completed = true; } finally { Volatile.Write(ref busy, 0); }
        }
        public void Reset()
        {
            Enter();
            try { ObjectDisposedException.ThrowIf(disposed, this); ReleasePictures(); completed = faulted = false; }
            finally { Volatile.Write(ref busy, 0); }
        }
        public void Dispose()
        {
            Enter(); try { disposed = true; ReleasePictures(); } finally { Volatile.Write(ref busy, 0); }
        }
        private void ReleasePictures()
        {
            output?.Dispose(); output = null; pictures?.Dispose(); pictures = null; format = null; geometry = null;
        }
        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The VP9 decoder requires reset or disposal after a failure."); }
        }
        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent VP9 codec operations are not supported."); }
        }
    }
}
