// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Ffv1
{
    internal class Ffv1Decoder(EncodedStreamInfo stream, Ffv1StreamConfiguration configuration, MediaBufferAllocator allocator)
        : IMediaCodec<MediaPacket, VideoFrame>
    {
        private Ffv1FrameModel? model;
        private readonly Ffv1SlicedPicture? slices = configuration.ExtraData.IsEmpty ? null :
            new(Ffv1SliceConfiguration.Read(configuration), allocator);
        private WritableMediaBuffer? contexts;
        private VideoFrame? pending;
        private bool complete, disposed, faulted;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket packet, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(packet);
            if (packet.StreamIndex != stream.StreamIndex)
            {
                throw new ArgumentException("The packet belongs to a different FFV1 stream.", nameof(packet));
            }
            Enter();
            try
            {
                EnsureActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (complete) { throw new InvalidOperationException("FFV1 input is complete."); }
                if (pending is not null) { return CodecInputStatus.OutputRequired; }

                bool decoded = false;
                try
                {
                    pending = Decode(packet, cancellationToken);
                    decoded = true;
                    return CodecInputStatus.Accepted;
                }
                finally
                {
                    if (!decoded)
                    {
                        faulted = true;
                        Release();
                    }
                }
            }
            finally { Exit(); }
        }

        private VideoFrame Decode(MediaPacket packet, CancellationToken token)
        {
            if (slices is not null) { return slices.Decode(stream, configuration, packet, token); }
            ReadOnlySpan<byte> data = packet.Data.Span;
            Ffv1RangeReader range = new(data);
            if (Ffv1FrameModel.ReadKeyFrame(ref range))
            {
                Initialize(Ffv1FrameModel.Read(ref range));
            }
            Ffv1FrameModel parameters = model ?? throw new InvalidDataException("FFV1 decoding requires an opening keyframe.");
            if (parameters.Transitions is byte[] transitions)
            {
                range.SetTransitions(transitions);
            }
            Ffv1RiceReader rice = new(parameters.Rice ? data[range.RiceOffset..] : []);
            VideoFormat format = Ffv1VideoFormat.Create(stream, configuration, parameters.PixelFormat);
            PlaneLayout[] layout = format.CreateTightLayout();
            int bytes = checked(layout[^1].Offset + layout[^1].Height * layout[^1].Stride);
            using WritableMediaBuffer pixels = allocator.Allocate(bytes);
            int contextBytes = contexts!.Memory.Length / 2;
            for (int plane = 0; plane < layout.Length; plane++)
            {
                Span<byte> state = plane == 0 ? contexts.Span[..contextBytes] : contexts.Span[contextBytes..];
                Ffv1PlaneDecoder.Decode(ref range, ref rice, pixels.Span, layout[plane], parameters.Rice, parameters.Quantizer, state, token);
            }
            using MediaBuffer buffer = pixels.Publish();
            return new(format, buffer, layout, packet.PresentationTime ?? packet.DecodeTime, packet.Duration);
        }

        private void Initialize(Ffv1FrameModel parameters)
        {
            int bytes = checked(2 * parameters.Quantizer.StateSize(parameters.Rice));
            if (contexts is null || contexts.Memory.Length != bytes)
            {
                contexts?.Dispose();
                contexts = allocator.Allocate(bytes);
            }
            int half = bytes / 2;
            parameters.Quantizer.InitializeState(contexts.Span[..half], parameters.Rice);
            parameters.Quantizer.InitializeState(contexts.Span[half..], parameters.Rice);
            model = parameters;
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                EnsureActive();
                cancellationToken.ThrowIfCancellationRequested();
                if (pending is not VideoFrame frame)
                {
                    return complete ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
                }
                pending = null;
                return CodecOutput<VideoFrame>.Available(frame);
            }
            finally { Exit(); }
        }

        public void CompleteInput()
        {
            Enter();
            try { EnsureActive(); complete = true; }
            finally { Exit(); }
        }

        public void Reset()
        {
            Enter();
            try { ObjectDisposedException.ThrowIf(disposed, this); Release(); complete = faulted = false; }
            finally { Exit(); }
        }

        public void Dispose()
        {
            Enter();
            try { disposed = true; Release(); }
            finally { Exit(); }
        }

        private void Release()
        {
            slices?.Reset();
            pending?.Dispose();
            pending = null;
            contexts?.Dispose();
            contexts = null;
            model = null;
        }

        private void EnsureActive()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The FFV1 decoder requires reset after failure."); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent FFV1 decoder operations are not supported.");
            }
        }

        private void Exit() => Volatile.Write(ref busy, 0);
    }
}
