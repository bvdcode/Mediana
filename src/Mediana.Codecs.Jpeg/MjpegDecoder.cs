// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal class MjpegDecoder(EncodedStreamInfo stream, MediaBufferAllocator allocator,
        JpegDecodeOptions options, Action<string>? warningHandler) : IMediaCodec<MediaPacket, VideoFrame>
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
                if (completed) { throw new InvalidOperationException("MJPEG input is complete."); }
                if (output is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != stream.StreamIndex)
                { throw new ArgumentException("The packet belongs to a different MJPEG stream.", nameof(packet)); }
                using JpegImage image = JpegDecoder.DecodeComponents(packet.Data, allocator, options, cancellationToken);
                if (image.ComponentCount is not (1 or 3)) { throw new NotSupportedException("MJPEG requires grayscale or three-component JPEG pictures."); }
                JpegColorSpace space = image.GetComponentColorSpace();
                VideoColorInfo color = new(Matrix: space == JpegColorSpace.Rgb ? MatrixCoefficients.Rgb : MatrixCoefficients.Bt601,
                    Range: ColorRange.Full, ChromaLocation: ChromaLocation.Center);
                color = stream.ColorDescription?.Resolve(color) ?? color;
                if ((space == JpegColorSpace.YCbCr && color.Matrix != MatrixCoefficients.Bt601) || color.Range != ColorRange.Full)
                { throw new NotSupportedException("This MJPEG component color matrix or range requires native component conversion."); }
                VideoFormat format = new(image.Width, image.Height, VideoPixelFormat.Rgba32,
                    color with { Matrix = MatrixCoefficients.Rgb, ChromaLocation = ChromaLocation.Unspecified },
                    stream.AspectRatioDescription?.Resolve(null));
                PlaneLayout[] planes = format.CreateTightLayout();
                using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[0].EndOffset));
                image.WriteRgbaTo(writable.Memory.Span, space, cancellationToken);
                foreach (string warning in image.Warnings) { warningHandler?.Invoke(warning); }
                using MediaBuffer buffer = writable.Publish();
                output = new(format, buffer, planes, packet.PresentationTime, packet.Duration);
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
            if (faulted) { throw new InvalidOperationException("The MJPEG decoder requires reset or disposal after a failure."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent MJPEG codec operations are not supported."); }
        }
    }
}
