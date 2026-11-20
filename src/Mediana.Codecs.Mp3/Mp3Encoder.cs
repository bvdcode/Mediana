// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

using Mediana.Formats.Audio;

using Mediana.Formats;

namespace Mediana.Codecs.Mp3
{
    /// <summary>Encodes contiguous interleaved Float32 mono/stereo PCM into complete MPEG Layer III frames.
    /// Retains at most one input lease. Packets are independently owned; operations must not overlap.</summary>
    public partial class Mp3Encoder : IMediaEncoder<AudioFrame>, IAudioEncoderTiming
    {
        private readonly int frameSamples;
        private readonly AudioFormat format;
        private readonly Mp3FrameHeader header;
        private readonly MediaBufferAllocator allocator;
        private readonly Mp3EncoderOptions options;
        private readonly long sampleTicks;
        private Mp3EncoderWorkspace? workspace;
        private AudioFrame? input;
        private int inputOffset;
        private int buffered;
        private long? firstTimestamp;
        private long packets;
        private long budgetRemainder;
        private bool completed;
        private bool drained;
        private bool disposed;
        private bool faulted;
        private int busy;

        private Mp3Encoder(AudioFormat format, Mp3FrameHeader header, EncodedStreamInfo stream,
            MediaBufferAllocator allocator, Mp3EncoderOptions options)
        {
            this.format = format;
            this.header = header;
            this.allocator = allocator;
            this.options = options;
            Stream = stream;
            frameSamples = header.SamplesPerFrame;
            sampleTicks = stream.TimeBase.Denominator / format.SampleRate;
        }

        public EncodedStreamInfo Stream { get; }
        public int PrimingSamples => Mp3AnalysisFilter.DelaySamples + Mp3HybridAnalysis.DelaySamples;
        public long InputSamplesPerChannel { get; private set; }
        public int? PaddingSamples => drained ? (InputSamplesPerChannel == 0 ? 0 : checked((int)(packets * frameSamples - InputSamplesPerChannel - PrimingSamples))) : null;

        public static Mp3Encoder Create(AudioFormat format, TimeBase clock, MediaBufferAllocator allocator,
            Mp3EncoderOptions? options = null, int streamIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            clock.Validate();
            if (format.SampleFormat != AudioSampleFormat.Float32 || format.Layout != AudioStorageLayout.Interleaved ||
                !(format.Channels.SequenceEqual(["front-center"]) || format.Channels.SequenceEqual(["mono"]) ||
                    format.Channels.SequenceEqual(["front-left", "front-right"])))
            {
                throw new NotSupportedException("MP3 encoding requires interleaved Float32 mono or stereo PCM.");
            }
            if (clock.Numerator != 1 || clock.Denominator % format.SampleRate != 0)
            {
                throw new NotSupportedException("The MP3 output clock must represent every input sample exactly.");
            }
            options ??= new();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumInputSamplesPerChannel);
            Mp3FrameHeader header = Mp3FrameHeader.Create(format.SampleRate, format.Channels.Count, options.BitRate);
            byte[] configuration = new byte[4];
            header.Write(configuration);
            EncodedStreamInfo stream = new(streamIndex, MediaKind.Audio, Mp3FrameHeader.CodecId, StreamFormats.Mp3, clock, configuration);
            return new(format, header, stream, allocator, options);
        }

        public CodecInputStatus TrySend(AudioFrame frame, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureUsable();
                ArgumentNullException.ThrowIfNull(frame);
                cancellationToken.ThrowIfCancellationRequested();
                if (completed)
                {
                    throw new InvalidOperationException("MP3 encoding input is complete.");
                }
                if (input is not null || buffered == frameSamples)
                {
                    success = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (frame.Format.SampleRate != format.SampleRate || frame.Format.SampleFormat != format.SampleFormat ||
                    frame.Format.Layout != format.Layout || !frame.Format.Channels.SequenceEqual(format.Channels) ||
                    frame.SamplesPerChannel > options.MaximumInputSamplesPerChannel || frame.Timestamp is not MediaTimestamp timestamp)
                {
                    throw new NotSupportedException("MP3 input must keep its configured PCM format, known timestamp and input sample budget.");
                }
                long start = timestamp.Rescale(Stream.TimeBase, TimestampRounding.TowardZero).Value;
                if (new MediaTimestamp(start, Stream.TimeBase) != timestamp ||
                    (firstTimestamp is long first && start != checked(first + InputSamplesPerChannel * sampleTicks)))
                {
                    throw new InvalidDataException("MP3 input timestamps must be sample-exact and contiguous.");
                }
                firstTimestamp ??= start;
                input = frame.Retain();
                inputOffset = 0;
                InputSamplesPerChannel = checked(InputSamplesPerChannel + frame.SamplesPerChannel);
                success = true;
                return CodecInputStatus.Accepted;
            }
            finally
            {
                FinishOperation(success);
            }
        }

        public CodecOutput<MediaPacket> Receive(CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureUsable();
                cancellationToken.ThrowIfCancellationRequested();
                CodecOutput<MediaPacket> result = ReceiveCore(cancellationToken);
                success = true;
                return result;
            }
            finally
            {
                FinishOperation(success);
            }
        }

        public void CompleteInput()
        {
            Enter();
            try
            {
                EnsureUsable();
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
                Clear();
                completed = drained = faulted = false;
                firstTimestamp = null;
                InputSamplesPerChannel = packets = 0;
                budgetRemainder = 0;
                buffered = inputOffset = 0;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private void Clear()
        {
            input?.Dispose();
            input = null;
            workspace?.Dispose();
            workspace = null;
        }

        private void FinishOperation(bool success)
        {
            if (!success)
            {
                faulted = true;
                Clear();
            }
            Volatile.Write(ref busy, 0);
        }

        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted)
            {
                throw new InvalidOperationException("The MP3 encoder has failed and requires reset or disposal.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent MP3 encoder operations are not supported.");
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                Clear();
                disposed = true;
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }
    }
}
