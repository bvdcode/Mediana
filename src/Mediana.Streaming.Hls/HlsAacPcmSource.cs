// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Streaming.Hls
{
    /// <summary>Registered container input, exact declared presentation selection and reusable mono/stereo Float32 PCM preparation.
    /// Borrows the source, shares the supplied allocator, and transfers each returned frame to the caller. Operations must not overlap.</summary>
    public class HlsAacPcmSource : IAsyncDisposable
    {
        private readonly AudioSession session;
        private readonly AudioTrimmer trimmer;
        private readonly MediaBufferAllocator allocator;
        private readonly AudioEncodingPcm converter;
        private AudioFrame? prepared;
        public AudioFormat Format { get; }
        public TimeBase TimeBase { get; }

        private HlsAacPcmSource(AudioSession session, AudioTrimmer trimmer, MediaBufferAllocator allocator,
            AudioEncodingPcm converter, AudioFrame first)
        {
            this.session = session;
            this.trimmer = trimmer;
            this.allocator = allocator;
            this.converter = converter;
            prepared = first;
            Format = first.Format;
            TimeBase = first.Timestamp?.TimeBase ?? throw new NotSupportedException("AAC encoding requires known PCM timestamps.");
        }

        public static async ValueTask<HlsAacPcmSource> OpenAsync(MediaEngine engine, IRandomAccessByteSource source, int streamIndex,
            int? channels, MediaBufferAllocator allocator, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(allocator);
            await using ContainerSession container = await engine.OpenContainerAsync(source, allocator,
                new() { BufferBudgetBytes = allocator.LimitBytes }, token).ConfigureAwait(false);
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
                AudioEncodingPcm converter = new(selected.Format, channels);
                converted = converter.Apply(selected, allocator, token);
                HlsAacPcmSource result = new(session, trimmer, allocator, converter, converted);
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

        public async ValueTask<AudioFrame?> ReadFrameAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (prepared is not null)
            {
                AudioFrame result = prepared;
                prepared = null;
                return result;
            }
            using AudioFrame? frame = await trimmer.ReadFrameAsync(token).ConfigureAwait(false);
            return frame is null ? null : converter.Apply(frame, allocator, token);
        }

        public async ValueTask DisposeAsync()
        {
            prepared?.Dispose();
            prepared = null;
            await trimmer.DisposeAsync().ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
    }
}
