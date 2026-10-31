// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Cli
{
    internal class HlsAacPcmSource(AudioSession session, AudioTrimmer trimmer,
        MediaBufferAllocator allocator, AudioEncodingPcm converter, AudioFrame first) : IAsyncDisposable
    {
        private AudioFrame? prepared = first;
        public AudioFormat Format { get; } = first.Format;
        public TimeBase TimeBase { get; } = first.Timestamp?.TimeBase
            ?? throw new NotSupportedException("AAC encoding requires known PCM timestamps.");

        public static async ValueTask<HlsAacPcmSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
            int? channels, MediaBufferAllocator allocator, CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, cancellationToken: token);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(streamIndex, reader.Streams.Count);
            Mp4TrackInfo track = reader.Tracks[streamIndex];
            if (track.Stream.Kind != MediaKind.Audio || track.SampleCount == 0)
            {
                throw new NotSupportedException("HLS AAC encoding requires a nonempty supported audio track.");
            }
            AudioSession session = await AudioEngineFactory.Create().OpenAudioAsync(source, streamIndex, cancellationToken: token);
            AudioFrame? original = null;
            AudioFrame? converted = null;
            AudioTrimmer? trimmer = null;
            bool success = false;
            try
            {
                original = await session.ReadFrameAsync(token) ?? throw new InvalidDataException("The audio track contains no decoded samples.");
                Mp4AudioSampleRange? range = track.Edits.Count == 0 ? null : Mp4AudioSampleRange.FromEditList(track, original.Format.SampleRate);
                trimmer = new(ReadAsync, range?.StartSample ?? 0, range?.SampleCount, allocator, range?.PresentationTime ?? original.Timestamp);
                using AudioFrame selected = await trimmer.ReadFrameAsync(token) ?? throw new InvalidDataException("The audio edit contains no samples.");
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
                        await trimmer.DisposeAsync();
                    }
                    await session.DisposeAsync();
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
            using AudioFrame? frame = await trimmer.ReadFrameAsync(token);
            return frame is null ? null : converter.Apply(frame, allocator, token);
        }

        public async ValueTask DisposeAsync()
        {
            prepared?.Dispose();
            prepared = null;
            await trimmer.DisposeAsync();
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("HLS audio decoding buffers were not released.");
            }
            GC.SuppressFinalize(this);
        }
    }
}
