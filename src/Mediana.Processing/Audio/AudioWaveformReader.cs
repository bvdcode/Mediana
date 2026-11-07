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
        private readonly MonoAudioReader resampler;
        private readonly AudioLevelAccumulator accumulator;
        private readonly Action<AudioLevel> emit;
        private AudioLevel? pending;
        private bool ended;
        private bool disposed;
        private bool faulted;
        private int busy;

        public AudioWaveformReader(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            ReadOnlySpan<double> channelGains, AudioWaveformOptions options, MediaBufferAllocator allocator)
            : this(readFrame, channelGains, null, options, allocator)
        {
        }

        /// <summary>Uses the supplied immutable mono mixer directly, before resampling and quantization.
        /// Its input format must match every callback frame; no second mixing frame is created.</summary>
        public AudioWaveformReader(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            AudioChannelMixer mixer, AudioWaveformOptions options, MediaBufferAllocator allocator)
            : this(readFrame, [], mixer ?? throw new ArgumentNullException(nameof(mixer)), options, allocator)
        {
        }

        private AudioWaveformReader(Func<CancellationToken, ValueTask<AudioFrame?>> readFrame,
            ReadOnlySpan<double> channelGains, AudioChannelMixer? mixer, AudioWaveformOptions options,
            MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(readFrame);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(allocator);
            options.Validate();
            accumulator = new AudioLevelAccumulator(options.SamplesPerWindow);
            emit = Emit;
            AudioResampleOptions resampling = options.Resampling with
            {
                SamplesPerFrame = Math.Min(options.SamplesPerWindow, options.Resampling.SamplesPerFrame)
            };
            resampler = mixer is null
                ? new MonoAudioReader(readFrame, channelGains, options.SampleRate, allocator, options.SampleFormat, resampling)
                : new MonoAudioReader(readFrame, mixer, options.SampleRate, allocator, options.SampleFormat, resampling);
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
                    else { accumulator.Process(frame, emit, cancellationToken); }
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
