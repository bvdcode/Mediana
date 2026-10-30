// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Cli
{
    internal class HlsAacAudioSource(AudioSession session, AudioTrimmer trimmer, AacEncoder encoder,
        MediaBufferAllocator allocator, AudioFrame first) : HlsAudioSource
    {
        private AudioFrame? prepared = first;
        private bool completed;
        public override EncodedStreamInfo Stream => encoder.Stream;
        public long InputSamplesPerChannel => encoder.InputSamplesPerChannel;
        public int PrimingSamples => encoder.PrimingSamples;
        public int? PaddingSamples => encoder.PaddingSamples;

        public static async ValueTask<HlsAacAudioSource> OpenAsync(IRandomAccessByteSource source, int streamIndex,
            int bitRate, MediaBufferAllocator allocator, CancellationToken token)
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
            AacEncoder? encoder = null;
            bool success = false;
            try
            {
                original = await session.ReadFrameAsync(token) ?? throw new InvalidDataException("The audio track contains no decoded samples.");
                Mp4AudioSampleRange? range = track.Edits.Count == 0 ? null : Mp4AudioSampleRange.FromEditList(track, original.Format.SampleRate);
                trimmer = new(ReadAsync, range?.StartSample ?? 0, range?.SampleCount, allocator, range?.PresentationTime ?? original.Timestamp);
                using AudioFrame selected = await trimmer.ReadFrameAsync(token) ?? throw new InvalidDataException("The audio edit contains no samples.");
                converted = AudioSampleConverter.Apply(selected, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator, token);
                encoder = AacEncoder.Create(converted.Format,
                    converted.Timestamp?.TimeBase ?? throw new NotSupportedException("AAC encoding requires known PCM timestamps."),
                    allocator, new() { BitRate = bitRate }, streamIndex);
                HlsAacAudioSource result = new(session, trimmer, encoder, allocator, converted);
                success = true;
                return result;
            }
            finally
            {
                original?.Dispose();
                if (!success)
                {
                    converted?.Dispose();
                    encoder?.Dispose();
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

        protected override async ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token)
        {
            while (true)
            {
                CodecOutput<MediaPacket> result = encoder.Receive(token);
                switch (result.Status)
                {
                    case CodecOutputStatus.Available:
                        return result.Value;
                    case CodecOutputStatus.EndOfStream:
                        return null;
                    case CodecOutputStatus.InputRequired:
                        if (completed)
                        {
                            throw new InvalidOperationException("The completed AAC encoder requested more PCM.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException("The AAC encoder returned an invalid output status.");
                }
                using AudioFrame? frame = prepared ?? await ReadConvertedAsync(token);
                prepared = null;
                if (frame is null)
                {
                    encoder.CompleteInput();
                    completed = true;
                }
                else if (encoder.TrySend(frame, token) != CodecInputStatus.Accepted)
                {
                    throw new InvalidOperationException("The AAC encoder did not accept its requested PCM block.");
                }
            }
        }

        private async ValueTask<AudioFrame?> ReadConvertedAsync(CancellationToken token)
        {
            using AudioFrame? frame = await trimmer.ReadFrameAsync(token);
            return frame is null ? null : AudioSampleConverter.Apply(frame, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator, token);
        }

        protected override async ValueTask DisposeCoreAsync()
        {
            prepared?.Dispose();
            prepared = null;
            encoder.Dispose();
            await trimmer.DisposeAsync();
            await session.DisposeAsync();
            if (session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("HLS audio decoding buffers were not released.");
            }
        }
    }
}
