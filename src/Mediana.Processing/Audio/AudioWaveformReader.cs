// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    /// <summary>Pulls owned PCM frames and emits fixed-size mono RMS/peak windows, including the final partial window.
    /// Mixing, resampling and optional quantization use the supplied media-buffer budget; the full recording is not retained.
    /// The callback's source remains borrowed. Trimming, gain normalization, tapering and rendering belong to the caller.
    /// Failure or cancellation makes the reader terminal and releases its owned frames and filter storage.</summary>
    public class AudioWaveformReader : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<AudioFrame?>> readFrame;
        private readonly double[] channelGains;
        private readonly AudioWaveformOptions options;
        private readonly MediaBufferAllocator allocator;
        private readonly AudioResampler resampler;
        private readonly AudioLevelAccumulator accumulator;
        private readonly Action<AudioLevel> emit;
        private AudioChannelMixer? mixer;
        private AudioLevel? pending;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public AudioWaveformReader(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            ReadOnlySpan<double> channelGains, AudioWaveformOptions options, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(readFrame);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(allocator);
            options.Validate();
            if (channelGains.IsEmpty)
            {
                throw new ArgumentException("An explicit mono coefficient is required for every input channel.", nameof(channelGains));
            }
            foreach (double gain in channelGains)
            {
                if (!double.IsFinite(gain))
                {
                    throw new ArgumentException("Channel gains must be finite.", nameof(channelGains));
                }
            }
            this.readFrame = readFrame;
            this.channelGains = channelGains.ToArray();
            this.options = options;
            this.allocator = allocator;
            accumulator = new AudioLevelAccumulator(options.SamplesPerWindow);
            emit = Emit;
            resampler = new AudioResampler(ReadMixedAsync, options.SampleRate, allocator, options.Resampling with
            {
                SamplesPerFrame = Math.Min(options.SamplesPerWindow, options.Resampling.SamplesPerFrame)
            });
        }

        public async ValueTask<AudioLevel?> ReadLevelAsync(CancellationToken cancellationToken = default)
        {
            Enter();
            bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted)
                {
                    throw new InvalidOperationException("The audio waveform reader has failed and must be disposed.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                while (pending is null && !ended)
                {
                    using AudioFrame? frame = await resampler.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                    if (frame is null)
                    {
                        accumulator.Complete(emit, cancellationToken);
                        ended = true;
                    }
                    else if (frame.Format.SampleFormat == options.SampleFormat)
                    {
                        accumulator.Process(frame, emit, cancellationToken);
                    }
                    else
                    {
                        using AudioFrame converted = AudioSampleConverter.Apply(frame, options.SampleFormat,
                            AudioStorageLayout.Interleaved, allocator, cancellationToken);
                        accumulator.Process(converted, emit, cancellationToken);
                    }
                }
                AudioLevel? result = pending;
                pending = null;
                succeeded = true;
                return result;
            }
            finally
            {
                try
                {
                    if (!succeeded)
                    {
                        faulted = true;
                        pending = null;
                        await resampler.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    Volatile.Write(ref busy, 0);
                }
            }
        }

        private async ValueTask<AudioFrame?> ReadMixedAsync(CancellationToken cancellationToken)
        {
            using AudioFrame? frame = await readFrame(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                return null;
            }
            mixer ??= new AudioChannelMixer(frame.Format, ["mono"], channelGains);
            return mixer.Apply(frame, allocator, cancellationToken);
        }

        private void Emit(AudioLevel level)
        {
            if (pending is not null)
            {
                throw new InvalidOperationException("The waveform reader produced multiple pending windows.");
            }
            pending = level;
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent waveform reader operations are not supported.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                disposed = true;
                pending = null;
                await resampler.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
            GC.SuppressFinalize(this);
        }
    }
}
