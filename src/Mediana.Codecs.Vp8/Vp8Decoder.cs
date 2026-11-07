// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Vp8
{
    internal class Vp8Decoder(EncodedStreamInfo stream, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly Vp8CodingState state = new();
        private readonly Vp8ReferencePictures references = new();
        private WritableMediaBuffer? segmentMap;
        private VideoFormat? format;
        private VideoFrame? output;
        private bool completed, disposed, faulted;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            Enter(); bool success = false;
            try
            {
                EnsureUsable(); ArgumentNullException.ThrowIfNull(packet); cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("VP8 input is complete."); }
                if (output is not null) { success = true; return CodecInputStatus.OutputRequired; }
                if (packet.StreamIndex != stream.StreamIndex) { throw new ArgumentException("The packet belongs to a different VP8 stream.", nameof(packet)); }
                Vp8UncompressedHeader tag = Vp8UncompressedHeader.Parse(packet.Data.Span);
                if (tag.KeyFrame) { OpenPicture(tag); }
                if (format is null || segmentMap is null) { throw new InvalidDataException("VP8 decoding requires an opening keyframe."); }
                Vp8FrameHeader header = new(packet.Data, state, cancellationToken);
                int stride = checked((format.Width + 15) / 16 * 16), paddedHeight = checked((format.Height + 15) / 16 * 16);
                int yLength = checked(stride * paddedHeight), uvLength = yLength / 4;
                using WritableMediaBuffer writable = allocator.Allocate(checked(yLength + 2 * uvLength));
                using (MediaBufferWorkspace workspace = new(allocator))
                {
                    Vp8ImageDecoder.Decode(header, writable.Span, format.Width, format.Height, workspace,
                        segmentMap.Span, references.GetMemory(), cancellationToken);
                }
                using MediaBuffer picture = writable.Publish();
                references.Update(header, picture);
                if (tag.ShowFrame)
                {
                    PlaneLayout[] planes =
                    [new(0, stride, format.Width, format.Height),
                     new(yLength, stride / 2, (format.Width + 1) / 2, (format.Height + 1) / 2),
                     new(yLength + uvLength, stride / 2, (format.Width + 1) / 2, (format.Height + 1) / 2)];
                    output = new(format, picture, planes, packet.PresentationTime, packet.Duration);
                }
                success = true; return CodecInputStatus.Accepted;
            }
            finally
            {
                if (!success) { faulted = true; ReleasePictures(); }
                Volatile.Write(ref busy, 0);
            }
        }

        private void OpenPicture(Vp8UncompressedHeader tag)
        {
            references.Dispose(); segmentMap?.Dispose(); segmentMap = null;
            segmentMap = allocator.Allocate(checked((tag.Width + 15) / 16 * ((tag.Height + 15) / 16)));
            SampleAspectRatio horizontal = Scale(tag.HorizontalScale), vertical = Scale(tag.VerticalScale);
            SampleAspectRatio aspect = new(horizontal.Numerator * vertical.Denominator, horizontal.Denominator * vertical.Numerator);
            VideoColorInfo color = new(Matrix: MatrixCoefficients.Bt601, Range: ColorRange.Limited, ChromaLocation: ChromaLocation.Center);
            format = new(tag.Width, tag.Height, VideoPixelFormat.Yuv420P8, color,
                stream.AspectRatioDescription?.Resolve(aspect) ?? aspect, stream.ColorDescription);
        }

        private static SampleAspectRatio Scale(int code) => code switch
        {
            0 => new(1, 1),
            1 => new(5, 4),
            2 => new(5, 3),
            3 => new(2, 1),
            _ => throw new InvalidDataException("Invalid VP8 display scale.")
        };

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
            Enter();
            try { EnsureUsable(); completed = true; }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Reset()
        {
            Enter();
            try { ObjectDisposedException.ThrowIf(disposed, this); ReleasePictures(); state.Reset(); completed = faulted = false; }
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
            output?.Dispose(); output = null; references.Dispose(); segmentMap?.Dispose(); segmentMap = null; format = null;
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The VP8 decoder requires reset or disposal after a failure."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Concurrent VP8 codec operations are not supported."); }
        }
    }
}
