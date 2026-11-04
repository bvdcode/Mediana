// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Processing.Audio;
using Mediana.Streaming.Hls;

namespace Mediana.Sample
{
    internal static class HlsSampleAudioPreparation
    {
        public static async Task<HlsPreparedAudio> RunAsync(string input, string directory,
            PreparedHlsSampleOptions options, CancellationToken token)
        {
            Directory.CreateDirectory(directory);
            MediaBufferAllocator allocator = new(options.BufferBudgetBytes);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            Mp4TrackInfo track;
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, cancellationToken: token))
            {
                track = reader.Tracks.First(track => track.Stream.Kind == MediaKind.Audio);
            }
            await using AudioSession session = await new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddAudioDecoder(new AacDecoderFactory()).Build().OpenAudioAsync(source, track.Stream.StreamIndex,
                    new() { BufferBudgetBytes = options.BufferBudgetBytes }, token);
            AudioFrame? original = await session.ReadFrameAsync(token)
                ?? throw new InvalidDataException("The input contains no decoded audio.");
            HlsPreparedAudio result;
            try
            {
                Mp4AudioSampleRange? edit = track.Edits.Count == 0 ? null
                    : Mp4AudioSampleRange.FromEditList(track, original.Format.SampleRate);
                MediaTimestamp origin = original.Timestamp
                    ?? throw new InvalidDataException("The audio has no presentation timestamps.");
                await using AudioTrimmer edited = AudioTrimmer.CreateTimeWindow(ReadOriginalAsync,
                    edit?.PresentationTime ?? origin, edit?.GetPresentationEnd(original.Format.SampleRate), allocator);
                await using AudioTrimmer selected = AudioTrimmer.CreateTimeWindow(edited.ReadFrameAsync,
                    options.Start, options.End, allocator);
                using AudioFrame first = await selected.ReadFrameAsync(token)
                    ?? throw new InvalidDataException("The selected interval contains no audio samples.");
                if (first.Timestamp != options.Start)
                {
                    throw new NotSupportedException("This sample requires audio beginning at presentation time zero.");
                }
                AudioChannelMixer downmix = AudioDownmix.CreateStereo(first.Format, AudioSampleFormat.Float32);
                using AudioFrame opening = downmix.Apply(first, allocator, token);
                int sampleRate = opening.Format.SampleRate;
                using AacEncoder encoder = AacEncoder.Create(opening.Format, opening.Timestamp!.Value.TimeBase,
                    allocator, new() { BitRate = options.AudioBitRate });
                await using AudioHlsWriter writer = AudioHlsWriter.Create(encoder.Stream,
                    (number, cancellationToken) => PreparedHlsSample.OpenSegmentAsync(directory, $"{number}.ts", cancellationToken),
                    allocator, new()
                    {
                        TargetDuration = options.TargetDuration,
                        MaximumSegments = options.MaximumSegments,
                        PlaylistTargetDurationSeconds = options.TargetSeconds
                    });
                bool pending = true;
                await MediaOperations.EncodeAudioAsync(ReadSelectedAsync, encoder, writer, cancellationToken: token);
                long expected = checked((long)(((Int128)options.EndMilliseconds * sampleRate + 999) / 1000));
                if (encoder.InputSamplesPerChannel != expected)
                {
                    throw new InvalidDataException("The input audio does not cover the selected presentation interval.");
                }
                HlsAudioRendition rendition = writer.CreateRendition("audio/index.m3u8", "audio", "Main")
                    with
                { IsDefault = true, AutoSelect = true };
                await File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"),
                    writer.CreateVodPlaylist(number => $"{number}.ts"), token);
                result = new(rendition, encoder.InputSamplesPerChannel, sampleRate,
                    encoder.PrimingSamples, encoder.PaddingSamples
                        ?? throw new InvalidOperationException("The AAC encoder did not report its completed padding."));

                async ValueTask<AudioFrame?> ReadSelectedAsync(CancellationToken cancellationToken)
                {
                    if (pending)
                    {
                        pending = false;
                        AudioFrame frame = opening.Retain();
                        opening.Dispose();
                        first.Dispose();
                        return frame;
                    }
                    using AudioFrame? samples = await selected.ReadFrameAsync(cancellationToken);
                    return samples is null ? null : downmix.Apply(samples, allocator, cancellationToken);
                }
            }
            finally
            {
                original?.Dispose();
            }
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Audio preparation retained media buffers after disposal.");
            }
            return result;

            ValueTask<AudioFrame?> ReadOriginalAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (original is null)
                {
                    return session.ReadFrameAsync(cancellationToken);
                }
                AudioFrame frame = original;
                original = null;
                return ValueTask.FromResult<AudioFrame?>(frame);
            }
        }
    }
}
