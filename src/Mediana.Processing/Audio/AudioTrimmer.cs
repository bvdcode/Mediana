// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Processing.Audio
{
    /// <summary>Selects a sample range from continuous PCM without retaining the full recording.
    /// The callback transfers ownership of input frames; its source remains borrowed.
    /// The constructor requires the complete requested range; CreateWindow selects its intersection with available samples.
    /// A null sample count reads through EOF.
    /// Original sample timestamps are preserved unless an explicit output origin is supplied.
    /// Whole input blocks retain their immutable storage; partial boundary blocks are copied into the supplied allocator.
    /// Stops reading at the selected end; the caller remains responsible for source disposal.</summary>
    public class AudioTrimmer : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<AudioFrame?>> readFrame;
        private readonly MediaBufferAllocator allocator;
        private readonly MediaTimestamp? outputOrigin;
        private readonly bool requireCompleteRange;
        private readonly MediaTimestamp? timeStart;
        private readonly MediaTimestamp? timeEnd;
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
            : this(readFrame, skipSamples, sampleCount, allocator, outputOrigin, true)
        {
        }

        /// <summary>Selects available samples in a half-open window starting at skipSamples.
        /// A finite maximumSampleCount bounds the window; null leaves its end open.
        /// Natural EOF ends the selection even when the window extends beyond the input.
        /// No samples are synthesized. Source failures remain terminal errors.</summary>
        public static AudioTrimmer CreateWindow(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            long skipSamples, long? maximumSampleCount, MediaBufferAllocator allocator, MediaTimestamp? outputOrigin = null)
        {
            return new AudioTrimmer(readFrame, skipSamples, maximumSampleCount, allocator, outputOrigin, false);
        }

        /// <summary>Selects available PCM sample instants in [start, end), preserving their original timestamps.
        /// A null end reads through EOF. Both boundaries round toward the next sample on the input's exact clock.
        /// Requires continuous known timestamps; seeking and container edits remain the caller's responsibility.
        /// An empty interval performs no source reads. Other intervals may read one initial block to establish the clock.</summary>
        public static AudioTrimmer CreateTimeWindow(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            MediaTimestamp start, MediaTimestamp? end, MediaBufferAllocator allocator)
        {
            return new AudioTrimmer(readFrame, start, end, allocator);
        }

        private AudioTrimmer(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            MediaTimestamp start, MediaTimestamp? end, MediaBufferAllocator allocator)
            : this(readFrame, 0, null, allocator, null, false)
        {
            start.TimeBase.Validate();
            end?.TimeBase.Validate();
            if (end is MediaTimestamp limit && limit.CompareTo(start) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(end), "The audio window end must not precede its start.");
            }
            timeStart = start;
            timeEnd = end;
            ended = end == start;
        }

        private AudioTrimmer(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame, long skipSamples,
            long? sampleCount, MediaBufferAllocator allocator, MediaTimestamp? outputOrigin, bool requireCompleteRange)
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
            this.requireCompleteRange = requireCompleteRange;
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
                    if (requireCompleteRange && (skipRemaining != 0 || remaining is > 0))
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
                    if (timeStart is MediaTimestamp startTime)
                    {
                        skipRemaining = inputClock.PositionAtOrAfter(startTime);
                        remaining = timeEnd is MediaTimestamp endTime
                            ? checked(inputClock.PositionAtOrAfter(endTime) - skipRemaining) : null;
                        if (remaining == 0)
                        {
                            ended = true;
                            return null;
                        }
                    }
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
                MediaTimestamp? timestamp = outputOrigin is null ? inputClock.AtOutput(start) : outputClock!.AtOutput(outputSamples);
                long nextOutputSamples = checked(outputSamples + count);
                AudioFrame result = Select(input, offset, count, timestamp, cancellationToken);
                outputSamples = nextOutputSamples;
                if (remaining is long value)
                {
                    remaining = value - count;
                }
                return result;
            }
        }

        private AudioFrame Select(AudioFrame input, int offset, int count, MediaTimestamp? timestamp, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (offset == 0 && count == input.SamplesPerChannel)
            {
                return input.RetainWithTimestamp(timestamp);
            }
            AudioFormat selectedFormat = input.Format;
            using WritableMediaBuffer writable = allocator.Allocate(selectedFormat.GetBufferSize(count));
            int bytesPerPlaneSample = selectedFormat.Layout switch
            {
                AudioStorageLayout.Interleaved => checked(selectedFormat.BytesPerSample * selectedFormat.Channels.Count),
                AudioStorageLayout.Planar => selectedFormat.BytesPerSample,
                _ => throw new ArgumentOutOfRangeException(nameof(input))
            };
            int planeBytes = checked(count * bytesPerPlaneSample);
            for (int plane = 0; plane < selectedFormat.PlaneCount; plane++)
            {
                token.ThrowIfCancellationRequested();
                input.GetPlane(plane).Span.Slice(checked(offset * bytesPerPlaneSample), planeBytes)
                    .CopyTo(writable.Memory.Span.Slice(checked(plane * planeBytes), planeBytes));
            }
            token.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new AudioFrame(selectedFormat, count, buffer, timestamp);
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
