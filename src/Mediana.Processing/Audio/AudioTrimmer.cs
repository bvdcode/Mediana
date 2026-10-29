// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    /// <summary>Selects a sample range from continuous PCM without retaining the full recording.
    /// The callback transfers ownership of input frames; its source remains borrowed.
    /// A null sample count reads through EOF. A finite range must exist completely in the input.
    /// Original sample timestamps are preserved unless an explicit output origin is supplied.
    /// Stops reading at the selected end; the caller remains responsible for source disposal.</summary>
    public class AudioTrimmer : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<AudioFrame?>> readFrame;
        private readonly MediaBufferAllocator allocator;
        private readonly MediaTimestamp? outputOrigin;
        private long skipRemaining;
        private long? remaining;
        private long inputSamples;
        private long outputSamples;
        private AudioFormat? format;
        private AudioSampleClock? inputClock;
        private AudioSampleClock? outputClock;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public AudioTrimmer(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame, long skipSamples,
            long? sampleCount, MediaBufferAllocator allocator, MediaTimestamp? outputOrigin = null)
        {
            ArgumentNullException.ThrowIfNull(readFrame);
            ArgumentOutOfRangeException.ThrowIfNegative(skipSamples);
            if (sampleCount is long count)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(count);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(count, long.MaxValue - skipSamples);
            }
            ArgumentNullException.ThrowIfNull(allocator);
            outputOrigin?.TimeBase.Validate();
            this.readFrame = readFrame;
            this.allocator = allocator;
            this.outputOrigin = outputOrigin;
            skipRemaining = skipSamples;
            remaining = sampleCount;
        }

        public async ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The audio trimmer has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                AudioFrame? result = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                succeeded = true;
                return result;
            }
            finally
            {
                if (!succeeded)
                {
                    faulted = true;
                }
                Volatile.Write(ref busy, 0);
            }
        }

        private async ValueTask<AudioFrame?> ReadCoreAsync(CancellationToken cancellationToken)
        {
            if (ended || remaining == 0)
            {
                return null;
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using AudioFrame? input = await readFrame(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (input is null)
                {
                    if (skipRemaining != 0 || remaining is > 0)
                    {
                        throw new InvalidDataException("The requested audio sample range extends past the input.");
                    }
                    ended = true;
                    return null;
                }
                if (format is null)
                {
                    format = input.Format;
                    inputClock = new AudioSampleClock(input.Timestamp, format.SampleRate, format.SampleRate);
                    outputClock = new AudioSampleClock(outputOrigin, format.SampleRate, format.SampleRate);
                }
                PcmSampleAccess.RequireFormat(input.Format, format);
                inputClock!.Validate(input.Timestamp, inputSamples);
                int offset = (int)Math.Min(skipRemaining, input.SamplesPerChannel);
                skipRemaining -= offset;
                int count = (int)Math.Min(input.SamplesPerChannel - offset, remaining ?? long.MaxValue);
                long start = checked(inputSamples + offset);
                inputSamples = checked(inputSamples + input.SamplesPerChannel);
                if (count == 0)
                {
                    continue;
                }
                using WritableMediaBuffer writable = allocator.Allocate(format.GetBufferSize(count));
                int bytesPerPlaneSample = format.Layout switch
                {
                    AudioStorageLayout.Interleaved => checked(format.BytesPerSample * format.Channels.Count),
                    AudioStorageLayout.Planar => format.BytesPerSample,
                    _ => throw new ArgumentOutOfRangeException(nameof(format.Layout))
                };
                int planeBytes = checked(count * bytesPerPlaneSample);
                for (int plane = 0; plane < format.PlaneCount; plane++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    input.GetPlane(plane).Span.Slice(checked(offset * bytesPerPlaneSample), planeBytes)
                        .CopyTo(writable.Memory.Span.Slice(checked(plane * planeBytes), planeBytes));
                }
                cancellationToken.ThrowIfCancellationRequested();
                MediaTimestamp? timestamp = outputOrigin is null ? inputClock.AtOutput(start) : outputClock!.AtOutput(outputSamples);
                outputSamples = checked(outputSamples + count);
                if (remaining is long value)
                {
                    remaining = value - count;
                }
                using MediaBuffer buffer = writable.Publish();
                return new AudioFrame(format, count, buffer, timestamp);
            }
        }

        public ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                GC.SuppressFinalize(this);
                return ValueTask.CompletedTask;
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
                throw new InvalidOperationException("Audio trimming operations must not overlap.");
            }
        }
    }
}
