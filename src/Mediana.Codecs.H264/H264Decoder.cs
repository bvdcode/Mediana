using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal class H264Decoder(int streamIndex, H264Configuration initialConfiguration,
        MediaBufferAllocator allocator, H264DecoderOptions options) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly H264Configuration startupConfiguration = initialConfiguration;
        private H264Configuration configuration = initialConfiguration;
        private H264ReferenceBuffer references = new();
        private H264FrameQueue frames = new();
        private H264PictureOrderState order = new();
        private bool inputCompleted;
        private bool endStream;
        private bool faulted;
        private bool disposed;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureAvailable();
                ArgumentNullException.ThrowIfNull(input);
                cancellationToken.ThrowIfCancellationRequested();
                if (inputCompleted)
                {
                    throw new InvalidOperationException("H.264 input has already been completed.");
                }
                if (frames.HasReady)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (input.StreamIndex != streamIndex)
                {
                    throw new ArgumentException("The packet belongs to another stream.", nameof(input));
                }
                if (endStream)
                {
                    throw new InvalidDataException("The H.264 stream has already ended.");
                }
                H264AccessUnit unit = H264AccessUnitReader.Read(input.Data, configuration, options, cancellationToken);
                if (!unit.Slice.IsEmpty)
                {
                    DecodePicture(unit, input, cancellationToken);
                }
                if (unit.EndSequence)
                {
                    frames.Drain();
                    references.Dispose();
                    references = new H264ReferenceBuffer();
                    order = new H264PictureOrderState();
                }
                cancellationToken.ThrowIfCancellationRequested();
                configuration = unit.Configuration;
                endStream = unit.EndStream;
                succeeded = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                Finish(succeeded);
            }
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureAvailable();
                cancellationToken.ThrowIfCancellationRequested();
                VideoFrame? frame = frames.Receive();
                CodecOutput<VideoFrame> output = frame is not null ? CodecOutput<VideoFrame>.Available(frame)
                    : inputCompleted ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
                succeeded = true;
                return output;
            }
            finally
            {
                Finish(succeeded);
            }
        }

        public void CompleteInput()
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureAvailable();
                frames.Drain();
                references.Dispose();
                inputCompleted = true;
                succeeded = true;
            }
            finally
            {
                Finish(succeeded);
            }
        }

        public void Reset()
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                frames.Dispose();
                references.Dispose();
                frames = new H264FrameQueue();
                references = new H264ReferenceBuffer();
                order = new H264PictureOrderState();
                configuration = startupConfiguration;
                inputCompleted = false;
                endStream = false;
                faulted = false;
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
                if (!disposed)
                {
                    disposed = true;
                    frames.Dispose();
                    references.Dispose();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private void DecodePicture(H264AccessUnit unit, MediaPacket packet, CancellationToken cancellationToken)
        {
            H264BitReader bits = H264Nal.Payload(unit.Slice.Span, H264Nal.Type(unit.Slice.Span));
            H264SliceHeader header = H264SliceHeaderReader.Read(ref bits, unit.Configuration, unit.Slice.Span[0], options);
            string? reason = H264Support.GetUnsupportedReason(unit.Configuration) ?? H264Support.GetUnsupportedReason(header);
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            H264PictureOrderState nextOrder = order.Advance(header);
            H264Picture picture = header.Kind switch
            {
                H264SliceKind.B => H264BidirectionalPictureDecoder.Decode(ref bits, header, references.GetBidirectionalLists(header, nextOrder.Current), nextOrder.Current, allocator, cancellationToken),
                H264SliceKind.I or H264SliceKind.P => H264PictureDecoder.Decode(ref bits, header, references.GetList0(header), allocator, cancellationToken),
                H264SliceKind.Sp or H264SliceKind.Si => throw new NotSupportedException("SP and SI pictures are not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(header))
            };
            VideoFrame? frame = null;
            bool stored = false;
            bool queued = false;
            try
            {
                frame = H264FrameExporter.Export(picture, allocator, packet.PresentationTime, packet.Duration, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                references.Store(picture, header, nextOrder.Current);
                stored = true;
                frames.Add(frame, nextOrder.Current, header);
                queued = true;
                order = nextOrder;
            }
            finally
            {
                if (!stored)
                {
                    picture.Dispose();
                }
                if (!queued)
                {
                    frame?.Dispose();
                }
            }
        }

        private void EnsureAvailable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("H.264 decoding has failed; reset or dispose the decoder.");
            }
        }

        private void Finish(bool succeeded)
        {
            try
            {
                if (!succeeded)
                {
                    faulted = true;
                    frames.Dispose();
                    references.Dispose();
                }
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent decoder operations are not supported.");
            }
        }
    }
}
