// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Streaming.Hls
{
    /// <summary>One indexed MP4 reader, exact movie-edit selection and reusable mono/stereo Float32 PCM preparation.
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
            (AudioSession session, Mp4TrackInfo track) = await OpenSessionAsync(engine, source, streamIndex, allocator, token).ConfigureAwait(false);
            AudioFrame? original = null;
            AudioFrame? converted = null;
            AudioTrimmer? trimmer = null;
            bool success = false;
            try
            {
                original = await session.ReadFrameAsync(token).ConfigureAwait(false) ?? throw new InvalidDataException("The audio track contains no decoded samples.");
                Mp4AudioSampleRange? range = track.Edits.Count == 0 ? null : Mp4AudioSampleRange.FromEditList(track, original.Format.SampleRate);
                trimmer = AudioTrimmer.CreateWindow(ReadAsync, range?.StartSample ?? 0, range?.SampleCount,
                    allocator, range?.PresentationTime ?? original.Timestamp);
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

        private static async ValueTask<(AudioSession Session, Mp4TrackInfo Track)> OpenSessionAsync(MediaEngine engine,
            IRandomAccessByteSource source, int streamIndex, MediaBufferAllocator allocator, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, cancellationToken: token).ConfigureAwait(false);
            bool transferred = false;
            try
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, reader.Streams.Count);
                Mp4TrackInfo track = reader.Tracks[streamIndex];
                if (track.Stream.Kind != MediaKind.Audio || track.SampleCount == 0)
                {
                    throw new NotSupportedException("HLS AAC encoding requires a nonempty supported audio track.");
                }
                AudioSession session = engine.OpenAudio(reader, allocator, streamIndex, cancellationToken: token);
                transferred = true;
                return (session, track);
            }
            finally { if (!transferred) { await reader.DisposeAsync().ConfigureAwait(false); } }
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
