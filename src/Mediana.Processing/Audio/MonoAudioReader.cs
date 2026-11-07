// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;

namespace Mediana.Processing.Audio
{
    /// <summary>Streams explicit mono mixing, resampling and quantization. Input frames transfer ownership;
    /// the source and allocator remain borrowed. Each output frame belongs to the caller.</summary>
    public class MonoAudioReader : IAsyncDisposable
    {
        private readonly Func<CancellationToken, ValueTask<AudioFrame?>> read;
        private readonly double[] gains;
        private readonly MediaBufferAllocator allocator;
        private readonly AudioSampleFormat sampleFormat;
        private readonly AudioResampler resampler;
        private AudioChannelMixer? mixer;
        private bool disposed;
        private bool faulted;
        private int busy;

        public MonoAudioReader(Func<CancellationToken, ValueTask<AudioFrame?>> read, ReadOnlySpan<double> gains,
            int sampleRate, MediaBufferAllocator allocator, AudioSampleFormat sampleFormat = AudioSampleFormat.Float64,
            AudioResampleOptions? resampling = null) : this(read, gains, null, sampleRate, allocator, sampleFormat, resampling) { }

        public MonoAudioReader(Func<CancellationToken, ValueTask<AudioFrame?>> read, AudioChannelMixer mixer,
            int sampleRate, MediaBufferAllocator allocator, AudioSampleFormat sampleFormat = AudioSampleFormat.Float64,
            AudioResampleOptions? resampling = null) : this(read, [], mixer ?? throw new ArgumentNullException(nameof(mixer)), sampleRate, allocator, sampleFormat, resampling) { }

        private MonoAudioReader(Func<CancellationToken, ValueTask<AudioFrame?>> read, ReadOnlySpan<double> gains,
            AudioChannelMixer? mixer, int sampleRate, MediaBufferAllocator allocator, AudioSampleFormat sampleFormat, AudioResampleOptions? resampling)
        {
            ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(allocator);
            if (mixer is null && gains.IsEmpty) { throw new ArgumentException("An explicit mono coefficient is required for every input channel.", nameof(gains)); }
            if (mixer is not null && mixer.OutputFormat.Channels.Count != 1) { throw new ArgumentException("The mixer must produce exactly one channel.", nameof(mixer)); }
            foreach (double gain in gains) { if (!double.IsFinite(gain)) { throw new ArgumentException("Channel gains must be finite.", nameof(gains)); } }
            if (!Enum.IsDefined(sampleFormat)) { throw new ArgumentOutOfRangeException(nameof(sampleFormat)); }
            this.read = read; this.gains = gains.ToArray(); this.mixer = mixer; this.allocator = allocator; this.sampleFormat = sampleFormat;
            resampler = new(ReadMixedAsync, sampleRate, allocator, resampling);
        }

        public async ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken cancellationToken = default)
        {
            Enter(); bool succeeded = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("A failed mono audio reader must be disposed."); }
                AudioFrame? frame = await resampler.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
                if (frame is null) { succeeded = true; return null; }
                bool transferred = false;
                try
                {
                    if (frame.Format.SampleFormat == sampleFormat && frame.Format.Layout == AudioStorageLayout.Interleaved)
                    { succeeded = true; transferred = true; return frame; }
                    AudioFrame result = AudioSampleConverter.Apply(frame, sampleFormat, AudioStorageLayout.Interleaved, allocator, cancellationToken);
                    succeeded = true; return result;
                }
                finally { if (!transferred) { frame.Dispose(); } }
            }
            finally
            {
                try { if (!succeeded) { faulted = true; await resampler.DisposeAsync().ConfigureAwait(false); } }
                finally { Volatile.Write(ref busy, 0); }
            }
        }

        private async ValueTask<AudioFrame?> ReadMixedAsync(CancellationToken cancellationToken)
        {
            using AudioFrame? frame = await read(cancellationToken).ConfigureAwait(false);
            if (frame is null) { return null; }
            mixer ??= new(frame.Format, ["mono"], gains);
            return mixer.Apply(frame, allocator, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try { disposed = true; await resampler.DisposeAsync().ConfigureAwait(false); }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) { throw new InvalidOperationException("Mono audio reader operations must not overlap."); }
        }
    }
}
