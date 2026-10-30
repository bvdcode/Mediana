// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Codecs.Aac
{
    /// <summary>Encodes contiguous mono/stereo interleaved Float32 PCM as AAC-LC raw access units.
    /// Uses long sine windows, independent channels and a per-frame bit ceiling without a reservoir.
    /// Retains at most one input lease; returned packets belong to the caller. Calls must not overlap.</summary>
    public partial class AacEncoder : IMediaCodec<AudioFrame, MediaPacket>
    {
        private const int FrameSamples = 1024;
        private readonly AudioFormat format;
        private readonly AacConfiguration configuration;
        private readonly MediaBufferAllocator allocator;
        private readonly AacEncoderOptions options;
        private readonly long sampleTicks;
        private AacEncoderWorkspace? workspace;
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

        private AacEncoder(AudioFormat format, AacConfiguration configuration, EncodedStreamInfo stream,
            MediaBufferAllocator allocator, AacEncoderOptions options)
        {
            this.format = format;
            this.configuration = configuration;
            this.allocator = allocator;
            this.options = options;
            Stream = stream;
            sampleTicks = stream.TimeBase.Denominator / format.SampleRate;
        }

        public EncodedStreamInfo Stream { get; }
        public int PrimingSamples => FrameSamples;
        public long InputSamplesPerChannel { get; private set; }
        public int? PaddingSamples => drained ? (int)((FrameSamples - InputSamplesPerChannel % FrameSamples) % FrameSamples) : null;

        public static AacEncoder Create(AudioFormat format, TimeBase clock, MediaBufferAllocator allocator,
            AacEncoderOptions? options = null, int streamIndex = 0)
        {
            ArgumentNullException.ThrowIfNull(format);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            clock.Validate();
            if (format.SampleFormat != AudioSampleFormat.Float32 || format.Layout != AudioStorageLayout.Interleaved ||
                !(format.Channels.SequenceEqual(["front-center"]) || format.Channels.SequenceEqual(["mono"]) ||
                    format.Channels.SequenceEqual(["front-left", "front-right"])))
            {
                throw new NotSupportedException("AAC encoding requires interleaved Float32 mono front-center or stereo front-left/front-right PCM.");
            }
            if (clock.Numerator != 1 || clock.Denominator % format.SampleRate != 0)
            {
                throw new NotSupportedException("The AAC output clock must represent every input sample exactly.");
            }
            AacConfiguration configuration = AacConfiguration.Create(format.SampleRate, format.Channels.Count);
            _ = AacBandTables.Get(configuration.FrequencyIndex, false);
            options ??= new();
            options.Validate(format.SampleRate, format.Channels.Count);
            EncodedStreamInfo stream = new(streamIndex, MediaKind.Audio, AacConfiguration.CodecId, clock, configuration.ToAudioSpecificConfig());
            return new(format, configuration, stream, allocator, options);
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
                    throw new InvalidOperationException("AAC encoding input is complete.");
                }
                if (input is not null || buffered == FrameSamples)
                {
                    success = true;
                    return CodecInputStatus.OutputRequired;
                }
                if (frame.Format.SampleRate != format.SampleRate || frame.Format.SampleFormat != format.SampleFormat ||
                    frame.Format.Layout != format.Layout || !frame.Format.Channels.SequenceEqual(format.Channels) ||
                    frame.SamplesPerChannel > options.MaximumInputSamplesPerChannel || frame.Timestamp is not MediaTimestamp timestamp)
                {
                    throw new NotSupportedException("AAC input must keep its configured PCM format, known timestamp and input sample budget.");
                }
                long start = timestamp.Rescale(Stream.TimeBase, TimestampRounding.TowardZero).Value;
                if (new MediaTimestamp(start, Stream.TimeBase) != timestamp ||
                    (firstTimestamp is long first && start != checked(first + InputSamplesPerChannel * sampleTicks)))
                {
                    throw new InvalidDataException("AAC input timestamps must be sample-exact and contiguous.");
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

        private CodecOutput<MediaPacket> ReceiveCore(CancellationToken token)
        {
            if (drained)
            {
                return CodecOutput<MediaPacket>.EndOfStream;
            }
            if (input is not null)
            {
                workspace ??= new(allocator, format.Channels.Count);
                int count = Math.Min(FrameSamples - buffered, input.SamplesPerChannel - inputOffset);
                ReadOnlySpan<float> pcm = MemoryMarshal.Cast<byte, float>(input.GetPlane(0).Span);
                for (int sample = 0; sample < count; sample++)
                {
                    if ((sample & 255) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                    }
                    for (int channel = 0; channel < format.Channels.Count; channel++)
                    {
                        float value = pcm[(inputOffset + sample) * format.Channels.Count + channel];
                        if (!float.IsFinite(value))
                        {
                            throw new InvalidDataException("AAC encoding requires finite PCM samples.");
                        }
                        workspace.Input(channel)[FrameSamples + buffered + sample] = value * 32768d;
                    }
                }
                buffered += count;
                inputOffset += count;
                if (inputOffset == input.SamplesPerChannel)
                {
                    input.Dispose();
                    input = null;
                }
            }
            bool final = completed && input is null && buffered == 0;
            if (buffered < FrameSamples && (!completed || input is not null))
            {
                return CodecOutput<MediaPacket>.InputRequired;
            }
            if (firstTimestamp is null)
            {
                drained = true;
                return CodecOutput<MediaPacket>.EndOfStream;
            }
            MediaPacket packet = Encode(token);
            packets++;
            buffered = 0;
            for (int channel = 0; channel < format.Channels.Count; channel++)
            {
                Span<double> samples = workspace!.Input(channel);
                samples[FrameSamples..].CopyTo(samples);
                samples[FrameSamples..].Clear();
            }
            if (final)
            {
                drained = true;
                workspace!.Dispose();
                workspace = null;
            }
            return CodecOutput<MediaPacket>.Available(packet);
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
                InputSamplesPerChannel = packets = budgetRemainder = 0;
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
                throw new InvalidOperationException("The AAC encoder has failed and requires reset or disposal.");
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent AAC encoder operations are not supported.");
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
