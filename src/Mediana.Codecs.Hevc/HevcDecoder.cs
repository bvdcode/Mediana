// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Hevc
{
    internal class HevcDecoder(int streamIndex, HevcConfiguration initialConfiguration, HevcDecoderOptions options,
        MediaBufferAllocator allocator, VideoColorDescription? color, VideoAspectRatioDescription? aspect) : IMediaCodec<MediaPacket, VideoFrame>
    {
        private readonly HevcConfiguration originalConfiguration = initialConfiguration;
        private HevcConfiguration configuration = initialConfiguration;
        private HevcReferenceBuffer references = new();
        private VideoFrameReorderQueue frames = new();
        private HevcPictureDecoder? pictures;
        private HevcPictureOrderCounter? orderCounter;
        private int log2Order;
        private bool firstPicture = true, noRaslOutput, inputCompleted, endStream, faulted, disposed;
        private int busy;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            Enter(); bool completed = false;
            try
            {
                EnsureAvailable(); ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
                if (inputCompleted || endStream) { throw new InvalidOperationException("HEVC input has already ended."); }
                if (input.StreamIndex != streamIndex) { throw new ArgumentException("The packet belongs to another stream.", nameof(input)); }
                if (frames.HasReady) { completed = true; return CodecInputStatus.OutputRequired; }
                HevcAccessUnit unit = HevcAccessUnitReader.Read(input.Data, configuration, options, cancellationToken);
                string? reason = unit.Configuration.GetUnsupportedReason();
                if (reason is not null) { throw new NotSupportedException(reason); }
                if (unit.Slices.Count != 0) { DecodePicture(unit.Slices, input, cancellationToken); }
                if (unit.EndSequence || unit.EndStream)
                {
                    frames.Drain(); references.Dispose(); references = new();
                    orderCounter = null; firstPicture = true; noRaslOutput = false;
                }
                cancellationToken.ThrowIfCancellationRequested();
                configuration = unit.Configuration; endStream = unit.EndStream;
                completed = true; return CodecInputStatus.Accepted;
            }
            finally { Finish(completed); }
        }

        private void DecodePicture(IReadOnlyList<HevcSliceHeader> slices, MediaPacket packet, CancellationToken cancellationToken)
        {
            HevcSliceHeader first = slices[0];
            bool irap = first.NalType is >= 16 and <= 21;
            if (firstPicture && !irap) { throw new InvalidDataException("HEVC decoding requires an initial IRAP picture."); }
            bool beginSequence = irap && (firstPicture || first.NalType is >= 16 and <= 20);
            int currentLog2 = first.Picture.Sequence.ReferencePictures.Log2PictureOrderCount;
            if (orderCounter is null || beginSequence)
            { orderCounter = new(currentLog2); log2Order = currentLog2; }
            else if (log2Order != currentLog2)
            { throw new InvalidDataException("HEVC picture order precision changed within a coded sequence."); }
            int order = orderCounter.Derive(first.NalType, first.TemporalId, first.PictureOrderCount, beginSequence);
            if (irap) { noRaslOutput = beginSequence; }
            // H.265 8.3.3 permits ignoring RASL pictures associated with a new random-access sequence.
            if (first.NalType is 8 or 9 && noRaslOutput) { return; }
            if (beginSequence)
            {
                references.Dispose(); references = new();
                frames.BeginSequence(first.NoOutputOfPriorPictures);
            }
            HevcReferenceSet selected = references.Select(first.References, order, log2Order);
            pictures ??= new(allocator);
            HevcReferencePicture picture = pictures.Decode(slices, selected, order, cancellationToken);
            bool stored = false, queued = false;
            VideoFrame? frame = null;
            try
            {
                if (first.PictureOutput) { frame = HevcFramePublisher.Publish(picture.Pixels, packet.PresentationTime, packet.Duration, color, aspect); }
                cancellationToken.ThrowIfCancellationRequested();
                if (irap || (first.NalType & 1) != 0)
                { references.Store(picture, first.Picture.Sequence.MaximumDecodedPictures); stored = true; }
                if (frame is not null)
                { frames.Add(frame, order, first.Picture.Sequence.MaximumReorderPictures); queued = true; }
                firstPicture = false;
            }
            finally
            {
                if (!stored) { picture.Dispose(); }
                if (!queued) { frame?.Dispose(); }
            }
        }

        public CodecOutput<VideoFrame> Receive(CancellationToken cancellationToken = default)
        {
            Enter(); bool completed = false;
            try
            {
                EnsureAvailable(); cancellationToken.ThrowIfCancellationRequested();
                VideoFrame? frame = frames.Receive();
                CodecOutput<VideoFrame> result = frame is not null ? CodecOutput<VideoFrame>.Available(frame)
                    : inputCompleted ? CodecOutput<VideoFrame>.EndOfStream : CodecOutput<VideoFrame>.InputRequired;
                completed = true; return result;
            }
            finally { Finish(completed); }
        }

        public void CompleteInput()
        {
            Enter(); bool completed = false;
            try
            {
                EnsureAvailable(); frames.Drain(); references.Dispose(); pictures?.Dispose(); pictures = null;
                inputCompleted = true; completed = true;
            }
            finally { Finish(completed); }
        }

        public void Reset()
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ReleaseState(); references = new(); frames = new(); configuration = originalConfiguration;
                orderCounter = null; firstPicture = true; noRaslOutput = false;
                inputCompleted = false; endStream = false; faulted = false;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                if (disposed) { return; }
                ReleaseState(); disposed = true;
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void EnsureAvailable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("HEVC decoding has failed; reset or dispose the decoder."); }
        }

        private void ReleaseState()
        {
            frames.Dispose(); references.Dispose(); pictures?.Dispose(); pictures = null;
        }

        private void Finish(bool completed)
        {
            try { if (!completed) { faulted = true; ReleaseState(); } }
            finally { Volatile.Write(ref busy, 0); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent HEVC decoder operations are not supported."); }
        }
    }
}
