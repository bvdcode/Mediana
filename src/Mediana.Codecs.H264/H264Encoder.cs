// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    /// <summary>Progressive Baseline CAVLC intra encoding with fixed QP and one independently decodable IDR per input.
    /// Borrows input frames; returns owned length-prefixed AVC packets. Calls and disposal must not overlap.</summary>
    public class H264Encoder : IMediaCodec<VideoFrame, MediaPacket>
    {
        private readonly MediaBufferAllocator allocator;
        private readonly H264EncoderOptions options;
        private readonly VideoFormat format;
        private readonly H264SequenceParameterSet sequence;
        private MediaPacket? pending;
        private MediaTimestamp? previousEnd;
        private bool completed;
        private bool disposed;
        private bool faulted;
        private int idrId;
        private int busy;

        private H264Encoder(VideoFormat format, EncodedStreamInfo stream, H264SequenceParameterSet sequence,
            MediaBufferAllocator allocator, H264EncoderOptions options)
        {
            this.format = format;
            Stream = stream;
            this.sequence = sequence;
            this.allocator = allocator;
            this.options = options;
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
            byte[] configuration = H264EncoderConfiguration.Create(format, allocator, options.Quantizer);
            H264Configuration parsed = H264Configuration.Parse(configuration, new H264DecoderOptions());
            EncodedStreamInfo stream = new(streamIndex, MediaKind.Video, "h264", timeBase, configuration, VideoDisplayMatrix.Identity);
            return new(format, stream, parsed.Sequences[0], allocator, options);
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
                using MediaBuffer encoded = H264IntraEncoder.Encode(input, sequence, options.Quantizer,
                    idrId, allocator, options.MaximumPacketBytes, cancellationToken);
                MediaDuration duration = input.Duration!.Value;
                if ((Int128)encoded.Memory.Length * 8 * duration.TimeBase.Denominator >
                    (Int128)H264EncoderConfiguration.MaximumBitsPerSecond * duration.Value * duration.TimeBase.Numerator ||
                    H264EncoderConfiguration.ExceedsAccessUnitLimit(checked(encoded.Memory.Length + Stream.Configuration.Length),
                        sequence.WidthInMbs * sequence.HeightInMapUnits))
                {
                    throw new NotSupportedException("The encoded H.264 picture exceeds the level 4.1 bitrate budget for its duration.");
                }
                pending = new MediaPacket(Stream.StreamIndex, encoded, timestamp, timestamp, input.Duration, isKeyFrame: true);
                long durationTicks = new MediaTimestamp(duration.Value, duration.TimeBase).Rescale(Stream.TimeBase, TimestampRounding.TowardZero).Value;
                previousEnd = new(checked(timestamp.Value + durationTicks), Stream.TimeBase);
                idrId ^= 1;
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
                previousEnd = null;
                idrId = 0;
                completed = false;
                faulted = false;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private MediaTimestamp ValidateFrame(VideoFrame input)
        {
            if (input.Format.Width != format.Width || input.Format.Height != format.Height ||
                input.Format.PixelFormat != format.PixelFormat || input.Format.Color != format.Color ||
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
            Int128 numerator = (Int128)duration.Value * duration.TimeBase.Numerator;
            int macroblocks = sequence.WidthInMbs * sequence.HeightInMapUnits;
            if (duration.Value == 0 || (Int128)macroblocks * duration.TimeBase.Denominator >
                numerator * H264EncoderConfiguration.MaximumMacroblocksPerSecond ||
                duration.TimeBase.Denominator > numerator * H264EncoderConfiguration.MaximumFramesPerSecond)
            {
                throw new NotSupportedException("The H.264 frame rate exceeds Baseline level 4.1.");
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
            }
            Volatile.Write(ref busy, 0);
        }

        private void ReleasePending()
        {
            pending?.Dispose();
            pending = null;
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
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
