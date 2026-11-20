// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana
{
    /// <summary>Registered container input, exact declared presentation selection and reusable mono/stereo Float32 PCM preparation.
    /// Borrows the source, shares the supplied allocator, and transfers each returned frame to the caller. Operations must not overlap.</summary>
    public class PreparedAudioSource : IAsyncDisposable
    {
        private readonly AudioSession session;
        private readonly AudioTrimmer trimmer;
        private readonly MediaBufferAllocator allocator;
        private readonly AudioEncodingPcm converter;
        private AudioFrame? prepared;
        private bool disposed;
        private bool faulted;
        private int busy;
        public AudioFormat Format { get; }
        public EncodedStreamInfo Stream => session.Stream;
        public TimeBase TimeBase { get; }

        private PreparedAudioSource(AudioSession session, AudioTrimmer trimmer, MediaBufferAllocator allocator,
            AudioEncodingPcm converter, AudioFrame first)
        {
            this.session = session;
            this.trimmer = trimmer;
            this.allocator = allocator;
            this.converter = converter;
            prepared = first;
            Format = first.Format;
            TimeBase = first.Timestamp?.TimeBase ?? throw new NotSupportedException("Audio encoding requires known PCM timestamps.");
        }

        public static async ValueTask<PreparedAudioSource> OpenAsync(MediaEngine engine, IByteSource source,
            MediaBufferAllocator allocator, AudioTranscodeOptions? options = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(allocator);
            options ??= new();
            ArgumentOutOfRangeException.ThrowIfNegative(options.StreamIndex ?? 0);
            await using ContainerSession container = await engine.OpenContainerAsync(source, allocator,
                options.Input, token).ConfigureAwait(false);
            int streamIndex = options.StreamIndex ?? container.Streams.FirstOrDefault(stream => stream.Kind == MediaKind.Audio)?.StreamIndex
                ?? throw new NotSupportedException("The input contains no stream of the requested media kind.");
            MediaTimeRange? range = container.GetPresentationRange(streamIndex, token);
            AudioSession session = engine.OpenAudio(container, streamIndex, token);
            AudioFrame? original = null;
            AudioFrame? converted = null;
            AudioTrimmer? trimmer = null;
            bool success = false;
            try
            {
                original = await session.ReadFrameAsync(token).ConfigureAwait(false) ?? throw new InvalidDataException("The audio track contains no decoded samples.");
                trimmer = range is MediaTimeRange selection
                    ? AudioTrimmer.CreateTimeWindow(ReadAsync, selection.Start, selection.End, allocator)
                    : AudioTrimmer.CreateWindow(ReadAsync, 0, null, allocator);
                using AudioFrame selected = await trimmer.ReadFrameAsync(token).ConfigureAwait(false) ?? throw new InvalidDataException("The audio edit contains no samples.");
                AudioEncodingPcm converter = new(selected.Format, options.Channels);
                converted = converter.Apply(selected, allocator, token);
                PreparedAudioSource result = new(session, trimmer, allocator, converter, converted);
                success = true;
                return result;
            }
            finally
            {
                original?.Dispose();
                if (!success)
                {
                    converted?.Dispose();
                    if (trimmer is not null)
                    {
                        await trimmer.DisposeAsync().ConfigureAwait(false);
                    }
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }

            ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (original is not null)
                {
                    AudioFrame result = original;
                    original = null;
                    return ValueTask.FromResult<AudioFrame?>(result);
                }
                return session.ReadFrameAsync(cancellationToken);
            }
        }

        public async ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken token = default)
        {
            Enter();
            bool success = false;
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (faulted) { throw new InvalidOperationException("The prepared audio source has failed and requires disposal."); }
                token.ThrowIfCancellationRequested();
                if (prepared is not null)
                {
                    AudioFrame result = prepared;
                    prepared = null;
                    success = true;
                    return result;
                }
                using AudioFrame? frame = await trimmer.ReadFrameAsync(token).ConfigureAwait(false);
                AudioFrame? converted = frame is null ? null : converter.Apply(frame, allocator, token);
                success = true;
                return converted;
            }
            finally { faulted |= !success; Volatile.Write(ref busy, 0); }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            { throw new InvalidOperationException("Concurrent prepared audio source operations are not supported."); }
        }

        public async ValueTask DisposeAsync()
        {
            Enter();
            try
            {
                if (disposed) { return; }
                disposed = true;
                prepared?.Dispose();
                prepared = null;
                try { await trimmer.DisposeAsync().ConfigureAwait(false); }
                finally { await session.DisposeAsync().ConfigureAwait(false); }
            }
            finally { Volatile.Write(ref busy, 0); }
            GC.SuppressFinalize(this);
        }
    }
}
