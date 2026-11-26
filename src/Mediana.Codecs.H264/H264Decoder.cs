// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal class H264Decoder(int streamIndex, H264Configuration initialConfiguration,
        MediaBufferAllocator allocator, H264DecoderOptions options, VideoColorDescription? colorDescription = null,
        VideoAspectRatioDescription? aspectRatioDescription = null, Action<string>? warningHandler = null)
        : IMediaCodec<MediaPacket, VideoFrame>, IVideoPresentationPrerollDecoder
    {
        private readonly H264Configuration startupConfiguration = initialConfiguration;
        private H264Configuration configuration = initialConfiguration;
        private H264ReferenceBuffer references = new();
        private H264FrameQueue frames = new(allocator);
        private H264PictureOrderState order = new();
        private MediaTimestamp? presentationStart;
        private bool reportedPictureTiming;
        private bool inputCompleted;
        private bool endStream;
        private bool faulted;
        private bool disposed;
        private int busy;

        public void SetPresentationStart(MediaTimestamp start)
        {
            start.TimeBase.Validate();
            Enter();
            try
            {
                EnsureAvailable();
                presentationStart = start;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

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
                if (unit.Slices.Count != 0)
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
                frames = new H264FrameQueue(allocator);
                references = new H264ReferenceBuffer();
                order = new H264PictureOrderState();
                configuration = startupConfiguration;
                presentationStart = null;
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
            IReadOnlyList<H264SliceRegion> slices = H264SliceHeaders.Read(unit, options, cancellationToken);
            H264SliceHeader header = slices[0].Header;
            string? reason = H264Support.GetUnsupportedReason(unit.Configuration);
            foreach (H264SliceRegion slice in slices)
            {
                reason ??= H264Support.GetUnsupportedReason(slice.Header);
            }
            if (reason is not null)
            {
                throw new NotSupportedException(reason);
            }
            H264PictureOrderState nextOrder = order.Advance(header);
            H264PictureTiming? timing = H264SeiReader.ReadPictureTiming(unit.PictureTimingNal.Span, header.Sequence.Vui, cancellationToken);
            VideoFramePresentation presentation = H264Presentation.Create(header, nextOrder.Current, timing,
                options.IgnoreInconsistentPictureTiming, out bool ignoredTiming);
            if (ignoredTiming && !reportedPictureTiming)
            {
                warningHandler?.Invoke("Ignored an inconsistent H.264 picture-timing display hint; presentation follows the coded picture structure and field order.");
                reportedPictureTiming = true;
            }
            if (IsDiscardablePreroll(header, packet, presentation))
            {
                foreach (H264SliceRegion slice in slices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _ = H264MultiSliceDecoder.GetReferences(references, slice.Header, nextOrder.Current);
                }
                references.DiscardNonReferenceFrame(header);
                frames.AddDiscarded(nextOrder.Current, header, cancellationToken);
                order = nextOrder;
                return;
            }
            H264Picture picture = H264MultiSliceDecoder.Decode(slices, references, nextOrder.Current, allocator, cancellationToken);
            VideoFrame? frame = null;
            bool stored = false;
            bool queued = false;
            try
            {
                frame = H264FramePublisher.Publish(picture, packet.PresentationTime, packet.Duration, cancellationToken,
                    colorDescription, aspectRatioDescription, presentation);
                cancellationToken.ThrowIfCancellationRequested();
                references.Store(picture, header, nextOrder.Current);
                stored = true;
                frames.Add(frame, nextOrder.Current, header, cancellationToken);
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

        private bool IsDiscardablePreroll(H264SliceHeader header, MediaPacket packet, VideoFramePresentation presentation)
        {
            if (presentationStart is not MediaTimestamp boundary || header.ReferenceIndicator != 0 || header.Kind != H264SliceKind.B ||
                !header.Sequence.FrameMbsOnly || header.Structure != H264PictureStructure.Frame ||
                presentation.Unit != VideoPresentationUnit.Frame || packet.PresentationTime is not MediaTimestamp start ||
                packet.Duration is not MediaDuration duration || duration.Value <= 0 || start.CompareTo(boundary) >= 0)
            {
                return false;
            }
            MediaTimestamp length = new(duration.Value, duration.TimeBase);
            TimeBase clock = TimeBase.Common(start.ReduceClock().TimeBase, length.ReduceClock().TimeBase);
            MediaTimestamp end = new(checked(start.Rescale(clock, TimestampRounding.TowardZero).Value +
                length.Rescale(clock, TimestampRounding.TowardZero).Value), clock);
            return end.CompareTo(boundary) <= 0;
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
