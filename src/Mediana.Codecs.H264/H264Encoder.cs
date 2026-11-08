// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

using Mediana.Formats;

namespace Mediana.Codecs.H264
{
    /// <summary>Progressive Baseline CAVLC I/P encoding with fixed QP or constrained variable rate and bounded reference storage.
    /// Borrows input frames; returns owned length-prefixed AVC packets. Calls and disposal must not overlap.</summary>
    public class H264Encoder : IMediaEncoder<VideoFrame>
    {
        private readonly MediaBufferAllocator allocator;
        private readonly H264EncoderOptions options;
        private readonly VideoFormat format;
        private readonly VideoColorInfo color;
        private readonly H264SequenceParameterSet sequence;
        private readonly H264LevelBudget levelBudget;
        private readonly H264RateController? rateController;
        private MediaPacket? pending;
        private MediaTimestamp? previousTimestamp;
        private MediaTimestamp? previousEnd;
        private MediaTimestamp? lastKeyFrame;
        private H264Picture? reference;
        private H264Picture? workspace;
        private H264MotionReference? motionReference;
        private int frameNumber;
        private bool completed;
        private bool disposed;
        private bool faulted;
        private int idrId;
        private int busy;

        private H264Encoder(VideoFormat format, EncodedStreamInfo stream, H264SequenceParameterSet sequence,
            MediaBufferAllocator allocator, H264EncoderOptions options, int parameterSetBytes)
        {
            this.format = format;
            color = format.GetColorInterpretation();
            Stream = stream;
            this.sequence = sequence;
            this.allocator = allocator;
            this.options = options;
            levelBudget = new(stream.TimeBase, sequence.WidthInMbs * sequence.HeightInMapUnits, parameterSetBytes);
            rateController = options.RateControl is H264RateControlOptions rate ? new(rate, stream.TimeBase, options.Quantizer) : null;
        }

        public EncodedStreamInfo Stream { get; }

        public static H264Encoder Create(VideoFormat format, TimeBase timeBase, MediaBufferAllocator allocator,
            H264EncoderOptions? options = null, int streamIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            timeBase.Validate();
            options ??= new();
            options.Validate();
            byte[] configuration = H264EncoderConfiguration.Create(format, allocator, options, out int parameterSetBytes);
            H264Configuration parsed = H264Configuration.Parse(configuration, new H264DecoderOptions());
            EncodedStreamInfo stream = new(streamIndex, MediaKind.Video, "h264", StreamFormats.Avc, timeBase, configuration, VideoDisplayMatrix.Identity);
            return new(format, stream, parsed.Sequences[0], allocator, options, parameterSetBytes);
        }

        public CodecInputStatus TrySend(VideoFrame input, CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureAvailable();
                ArgumentNullException.ThrowIfNull(input);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("H.264 encoder input has already been completed.");
                }
                if (pending is not null)
                {
                    succeeded = true;
                    return CodecInputStatus.OutputRequired;
                }
                MediaTimestamp timestamp = ValidateFrame(input);
                bool keyFrame = RequiresKeyFrame(timestamp);
                int number = keyFrame ? 0 : (frameNumber + 1) & 15;
                MediaDuration duration = input.Duration!.Value;
                long durationTicks = new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(Stream.TimeBase, TimestampRounding.TowardZero).Value;
                MediaTimestamp endTime = new(checked(timestamp.Value + durationTicks), Stream.TimeBase);
                int levelBytes = levelBudget.MaximumPacketBytes(timestamp.Value, keyFrame);
                if (levelBytes < 8)
                {
                    throw new NotSupportedException("The H.264 level 4.1 access-unit or coded-picture buffer budget cannot fit the picture.");
                }
                H264FrameRateBudget requested = rateController?.Prepare(timestamp, durationTicks, keyFrame) ?? new(options.MaximumPacketBytes, options.Quantizer);
                H264FrameRateBudget budget = new(Math.Min(requested.MaximumBytes, levelBytes), requested.Quantizer);
                if (!keyFrame && options.MotionSearchRange != 0)
                {
                    motionReference ??= new(reference!, options.MotionSearchRange, allocator);
                    motionReference.Prepare(reference!, cancellationToken);
                }
                using H264EncodedPicture encoded = H264PictureEncoder.Encode(input, sequence, reference, keyFrame, number, idrId,
                    allocator, options, budget, ref workspace, cancellationToken, motionReference);
                levelBudget.Commit(timestamp.Value, keyFrame, encoded.Data.Memory.Length);
                pending = new MediaPacket(Stream.StreamIndex, encoded.Data, timestamp, timestamp, input.Duration, isKeyFrame: keyFrame);
                rateController?.Commit(timestamp, encoded.Quantizer, encoded.Data.Memory.Length, keyFrame);
                previousTimestamp = timestamp;
                previousEnd = endTime;
                frameNumber = number;
                if (keyFrame)
                {
                    lastKeyFrame = timestamp;
                    idrId ^= 1;
                }
                if (options.KeyFrameInterval > TimeSpan.Zero)
                {
                    H264Picture? previousReference = reference;
                    reference = encoded.TakeReference();
                    workspace = previousReference;
                }
                else
                {
                    workspace = encoded.TakeReference();
                }
                succeeded = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                Finish(succeeded);
            }
        }

        public CodecOutput<MediaPacket> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                EnsureAvailable();
                cancellationToken.ThrowIfCancellationRequested();
                MediaPacket? packet = pending;
                pending = null;
                CodecOutput<MediaPacket> result = packet is not null ? CodecOutput<MediaPacket>.Available(packet)
                    : completed ? CodecOutput<MediaPacket>.EndOfStream : CodecOutput<MediaPacket>.InputRequired;
                succeeded = true;
                return result;
            }
            finally
            {
                Finish(succeeded);
            }
        }

        public void CompleteInput()
        {
            Enter();
            try
            {
                EnsureAvailable();
                completed = true;
                ReleasePictures();
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
                ReleasePending();
                ReleasePictures();
                previousTimestamp = null;
                previousEnd = null;
                lastKeyFrame = null;
                frameNumber = 0;
                idrId = 0;
                levelBudget.Reset();
                rateController?.Reset();
                completed = false;
                faulted = false;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private bool RequiresKeyFrame(MediaTimestamp timestamp)
        {
            if (reference is null || lastKeyFrame is not MediaTimestamp keyFrame || options.KeyFrameInterval == TimeSpan.Zero)
            {
                return true;
            }
            Int128 elapsed = (Int128)timestamp.Value - keyFrame.Value;
            return elapsed * Stream.TimeBase.Numerator * TimeSpan.TicksPerSecond >=
                (Int128)options.KeyFrameInterval.Ticks * Stream.TimeBase.Denominator;
        }

        private MediaTimestamp ValidateFrame(VideoFrame input)
        {
            if (input.Format.Width != format.Width || input.Format.Height != format.Height ||
                input.Format.PixelFormat != format.PixelFormat || input.Format.GetColorInterpretation() != color ||
                input.Format.SampleAspectRatio != format.SampleAspectRatio)
            {
                throw new NotSupportedException("The input frame differs from the H.264 encoder configuration.");
            }
            MediaTimestamp timestamp = input.Timestamp
                ?? throw new NotSupportedException("H.264 encoding requires input presentation timestamps.");
            MediaTimestamp exact = timestamp.Rescale(Stream.TimeBase, TimestampRounding.TowardZero);
            if (exact != timestamp || (previousEnd is MediaTimestamp end && timestamp.CompareTo(end) < 0))
            {
                throw new InvalidDataException("H.264 input timestamps must be exact in the stream clock and must not overlap the preceding frame.");
            }
            MediaDuration duration = input.Duration
                ?? throw new NotSupportedException("H.264 encoding requires input frame durations.");
            duration.TimeBase.Validate();
            MediaTimestamp durationTime = new(duration.Value, duration.TimeBase);
            if (durationTime.Rescale(Stream.TimeBase, TimestampRounding.TowardZero) != durationTime)
            {
                throw new InvalidDataException("H.264 input durations must be exact in the stream clock.");
            }
            if (duration.Value == 0)
            {
                throw new NotSupportedException("H.264 encoding requires positive input frame durations.");
            }
            if (previousTimestamp is MediaTimestamp previous)
            {
                Int128 elapsed = ((Int128)exact.Value - previous.Value) * Stream.TimeBase.Numerator;
                int macroblocks = sequence.WidthInMbs * sequence.HeightInMapUnits;
                if ((Int128)macroblocks * Stream.TimeBase.Denominator >
                    elapsed * H264EncoderConfiguration.MaximumMacroblocksPerSecond ||
                    Stream.TimeBase.Denominator > elapsed * H264EncoderConfiguration.MaximumFramesPerSecond)
                {
                    throw new NotSupportedException("The H.264 frame rate exceeds Baseline level 4.1.");
                }
            }
            return exact;
        }

        private void EnsureAvailable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The H.264 encoder has failed and must be reset or disposed.");
            }
        }

        private void Finish(bool succeeded)
        {
            if (!succeeded)
            {
                faulted = true;
                ReleasePending();
                ReleasePictures();
                previousTimestamp = null;
            }
            Volatile.Write(ref busy, 0);
        }

        private void ReleasePending()
        {
            pending?.Dispose();
            pending = null;
        }

        private void ReleasePictures()
        {
            reference?.Dispose();
            reference = null;
            workspace?.Dispose();
            workspace = null;
            motionReference?.Dispose();
            motionReference = null;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent H.264 encoder operations are not supported.");
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                disposed = true;
                ReleasePending();
                ReleasePictures();
                previousTimestamp = null;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
