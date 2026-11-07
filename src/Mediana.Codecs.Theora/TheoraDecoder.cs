// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora
{
    internal class TheoraDecoder : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly EncodedStreamInfo stream;
        private readonly TheoraConfiguration configuration;
        private readonly MediaBufferAllocator allocator;
        private readonly VideoFormat format;
        private readonly PlaneLayout[] planes;
        private TheoraPictureDecoder? pictures;
        private VideoFrame? output;
        private bool completed, disposed, faulted;
        private int busy;

        internal TheoraDecoder(EncodedStreamInfo stream, TheoraConfiguration configuration, MediaBufferAllocator allocator)
        {
            this.stream = stream; this.configuration = configuration; this.allocator = allocator;
            format = TheoraVideoFormat.Create(configuration.Identification, stream);
            planes = TheoraVideoFormat.Planes(configuration.Identification);
        }

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter(); bool success = false;
            try
            {
                EnsureUsable(); ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("Theora input is complete."); }
                if (output is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != stream.StreamIndex) { throw new ArgumentException("The packet belongs to a different Theora stream.", nameof(packet)); }
                pictures ??= new(configuration, allocator, cancellationToken);
                using MediaBuffer picture = pictures.Decode(packet.Data.Span, cancellationToken);
                output = new(format, picture, planes, packet.PresentationTime, packet.Duration);
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
        { Enter(); try { EnsureUsable(); completed = true; } finally { Volatile.Write(ref busy, 0); } }
        public void Reset()
        {
            Enter();
            try { ObjectDisposedException.ThrowIf(disposed, this); ReleasePictures(); completed = faulted = false; }
            finally { Volatile.Write(ref busy, 0); }
        }
        public void Dispose()
        { Enter(); try { disposed = true; ReleasePictures(); } finally { Volatile.Write(ref busy, 0); } }
        private void ReleasePictures()
        { output?.Dispose(); output = null; pictures?.Dispose(); pictures = null; }
        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The Theora decoder requires reset or disposal after a failure."); }
        }
        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent Theora codec operations are not supported."); }
        }
    }
}
