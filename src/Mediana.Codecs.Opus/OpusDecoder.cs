// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Opus
{
    internal class OpusDecoder(int streamIndex, OpusConfiguration configuration, MediaBufferAllocator allocator) : IMediaCodec<MediaPacket, AudioFrame>, IPacketPrerollDecoder
    {
        private static readonly TimeBase Clock = new(1, OpusConfiguration.ClockRate);
        private readonly AudioFormat format = OpusChannelLayout.Create(configuration);
        private readonly float gain = (float)Math.Pow(10, configuration.OutputGainQ8 / (20.0 * 256));
        private OpusDecoderWorkspace? workspace;
        private AudioFrame? pending;
        private int skipSamples = configuration.PreSkipSamples;
        private long prerollPackets;
        private bool disposed, faulted, completed;

        public int RequiredPrerollPackets => 0;

        public CodecInputStatus TrySend(MediaPacket input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input); EnsureUsable();
            bool succeeded = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (completed) { throw new InvalidOperationException("Opus input is complete."); }
                if (pending is not null) { succeeded = true; return CodecInputStatus.OutputRequired; }
                if (input.StreamIndex != streamIndex || input.Data.IsEmpty) { throw new InvalidDataException("The Opus packet is empty or belongs to a different stream."); }
                int samples = OpusPacketInfo.ReadTiming(input.Data.Span).SampleCount;
                int end = GetPresentationSamples(input.Duration, samples);
                workspace ??= new(configuration, allocator);
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(samples));
                Span<float> output = MemoryMarshal.Cast<byte, float>(writable.Span);
                workspace.Decode(input.Data.Span, samples, output, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                int start = Math.Min(skipSamples, samples);
                skipSamples -= start;
                if (prerollPackets > 0) { prerollPackets--; }
                else if (end > start)
                {
                    ApplyGain(output.Slice(start * configuration.ChannelCount, (end - start) * configuration.ChannelCount));
                    cancellationToken.ThrowIfCancellationRequested();
                    using MediaBuffer buffer = writable.Publish();
                    using MediaBuffer view = buffer.Slice(checked(start * configuration.ChannelCount * sizeof(float)), format.GetBufferSize(end - start));
                    MediaTimestamp? timestamp = input.PresentationTime;
                    if (timestamp is MediaTimestamp known)
                    {
                        timestamp = new(checked(known.Rescale(Clock, TimestampRounding.NearestEven).Value + start), Clock);
                    }
                    pending = new(format, end - start, view, timestamp);
                }
                succeeded = true; return CodecInputStatus.Accepted;
            }
            finally { if (!succeeded) { Fault(); } }
        }

        private static int GetPresentationSamples(MediaDuration? duration, int samples)
        {
            if (duration is null) { return samples; }
            MediaDuration value = duration.Value;
            Int128 numerator = (Int128)value.Value * value.TimeBase.Numerator * OpusConfiguration.ClockRate;
            Int128 count = numerator / value.TimeBase.Denominator;
            if (numerator % value.TimeBase.Denominator != 0 || count > samples)
            {
                throw new InvalidDataException("The Opus presentation duration must contain a whole number of samples within its coded packet.");
            }
            return (int)count;
        }

        private void ApplyGain(Span<float> output)
        {
            if (gain == 1) { return; }
            int at = 0;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<float> scale = new(gain);
                for (; at <= output.Length - Vector<float>.Count; at += Vector<float>.Count)
                {
                    (new Vector<float>(output[at..]) * scale).CopyTo(output[at..]);
                }
            }
            for (; at < output.Length; at++) { output[at] *= gain; }
        }

        public CodecOutput<AudioFrame> Receive(CancellationToken cancellationToken = default)
        {
            EnsureUsable();
            if (cancellationToken.IsCancellationRequested) { Fault(); cancellationToken.ThrowIfCancellationRequested(); }
            if (pending is not null)
            {
                AudioFrame frame = pending; pending = null; return CodecOutput<AudioFrame>.Available(frame);
            }
            return completed ? CodecOutput<AudioFrame>.EndOfStream : CodecOutput<AudioFrame>.InputRequired;
        }

        public void CompleteInput()
        {
            EnsureUsable();
            if (prerollPackets != 0 || skipSamples != 0)
            {
                Fault(); throw new InvalidDataException("Opus input ended before its priming or requested preroll was complete.");
            }
            completed = true; workspace?.Dispose(); workspace = null;
        }

        public void ResetForPreroll(long packetCount, bool startsAtStreamBeginning)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(packetCount);
            Reset(); prerollPackets = packetCount;
            if (!startsAtStreamBeginning) { skipSamples = 0; }
        }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Dispose(); pending = null; workspace?.Dispose(); workspace = null;
            skipSamples = configuration.PreSkipSamples; prerollPackets = 0; completed = false; faulted = false;
        }

        private void Fault() { Reset(); faulted = true; }
        private void EnsureUsable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (faulted) { throw new InvalidOperationException("The Opus decoder has failed and requires reset or disposal."); }
        }

        public void Dispose()
        {
            if (disposed) { return; }
            Reset(); disposed = true; GC.SuppressFinalize(this);
        }
    }
}
