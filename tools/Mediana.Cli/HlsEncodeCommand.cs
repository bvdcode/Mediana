// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Codecs.H264;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.IO;
using Mediana.Streaming.Hls;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class HlsEncodeCommand
    {
        private const long BufferBudgetBytes = 128 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, string output, int videoIndex, int? audioIndex,
            AudioCodecMode? audioCodec, int[] audioBitRates, int? audioChannels, int[] widths, H264RateControlOptions?[]? videoRates,
            int qp, long targetMilliseconds, bool separateAudio, TextWriter console, ILogger logger, CancellationToken token)
        {
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = await engine.OpenVideoAsync(source, videoIndex, cancellationToken: token);
            if (session.Stream.DisplayMatrix != VideoDisplayMatrix.Identity)
            {
                throw new NotSupportedException("Video encoding currently requires an identity display transform.");
            }
            using VideoFrame first = await session.ReadFrameAsync(token)
                ?? throw new InvalidDataException("The input contains no decoded video frames.");
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            int[] sourceRates = separateAudio ? audioBitRates.Distinct().ToArray() : audioBitRates;
            await using HlsAudioSource? audio = audioIndex is int index ? audioCodec switch
            {
                null => await HlsCopyAudioSource.OpenAsync(source, index, allocator, token),
                AudioCodecMode.Aac => await HlsAacAudioSource.OpenAsync(AudioEngineFactory.Create(), source, index, sourceRates, audioChannels, allocator, token),
                _ => throw new ArgumentOutOfRangeException(nameof(audioCodec))
            } : null;
            long frames = 0;
            int segments = 0;
            MediaDurationAccumulator processed = new();
            MediaProgress progress = new(logger, TimeProvider.System, session.Stream.CodedDuration);
            await CommandOutputDirectory.WriteAsync(output, async directory =>
            {
                List<HlsVideoRendition> renditions = [];
                List<HlsAudioOutput> audioOutputs = [];
                try
                {
                    if (separateAudio)
                    {
                        HlsAudioSource selected = audio ?? throw new ArgumentException("Separate audio requires a selected audio stream.", nameof(audioIndex));
                        for (int profile = 0; profile < sourceRates.Length; profile++)
                        {
                            audioOutputs.Add(HlsAudioOutput.Create(selected.GetStream(profile), allocator, targetMilliseconds, directory, profile));
                        }
                    }
                    HashSet<(int Width, int Height)> dimensions = [];
                    for (int variantIndex = 0; variantIndex < widths.Length; variantIndex++)
                    {
                        int width = widths[variantIndex];
                        HlsVideoRendition rendition = HlsVideoRendition.Create(first.Format, session.Stream, separateAudio ? null : audio?.GetStream(variantIndex),
                            allocator, width, qp, videoRates?[variantIndex], targetMilliseconds, directory);
                        renditions.Add(rendition);
                        if (!dimensions.Add((rendition.Format.Width, rendition.Format.Height)))
                        {
                            throw new ArgumentException("The selected width bounds produce duplicate encoded dimensions.", nameof(widths));
                        }
                    }
                    IContainerWriter[] audioDestinations = separateAudio
                        ? audioOutputs.Select(rendition => (IContainerWriter)rendition.Writer).ToArray()
                        : renditions.Select(rendition => (IContainerWriter)rendition.Writer).ToArray();
                    VideoFrame? frame = first;
                    while (frame is not null)
                    {
                        using (frame)
                        {
                            if (audio is not null)
                            {
                                await audio.WriteBeforeAsync(frame.Timestamp, audioDestinations, token);
                            }
                            foreach (HlsVideoRendition rendition in renditions)
                            {
                                await rendition.WriteAsync(frame, token);
                            }
                            frames = checked(frames + 1);
                            processed.Add(frame.Duration);
                            progress.Report(processed.Duration, BytesWritten());
                        }
                        frame = await session.ReadFrameAsync(token);
                    }
                    if (audio is not null)
                    {
                        await audio.WriteBeforeAsync(null, audioDestinations, token);
                    }
                    foreach (HlsVideoRendition rendition in renditions)
                    {
                        await rendition.CompleteAsync(token);
                    }
                    foreach (HlsAudioOutput audioOutput in audioOutputs)
                    {
                        await audioOutput.Writer.CompleteAsync(token);
                    }
                    ValidateAlignment(renditions);
                    await WritePlaylistsAsync(directory, renditions, audioOutputs, audioBitRates, sourceRates, token);
                    segments = renditions[0].Writer.Segments.Count;
                    progress.Report(processed.Duration, BytesWritten(), completed: true);

                    long BytesWritten() => checked(renditions.Sum(rendition => rendition.Writer.BytesWritten)
                        + audioOutputs.Sum(rendition => rendition.Writer.BytesWritten));
                }
                finally
                {
                    await Task.WhenAll(renditions.Select(rendition => rendition.DisposeAsync().AsTask())
                        .Concat(audioOutputs.Select(rendition => rendition.DisposeAsync().AsTask())));
                }
                if (audio is not null)
                {
                    await audio.DisposeAsync();
                }
                await session.DisposeAsync();
                if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
                {
                    throw new InvalidOperationException("HLS encoding buffers were not released.");
                }
            }, token);
            if (audio is HlsAacAudioSource encoded)
            {
                await console.WriteLineAsync($"AAC-LC: {encoded.InputSamplesPerChannel} input samples per channel; priming {encoded.PrimingSamples}; padding {encoded.PaddingSamples}; {encoded.EncoderCount} encoders.");
            }
            await console.WriteLineAsync($"HLS encoding: {frames} input frames; {audio?.PacketsWritten ?? 0} audio packets; {widths.Length} variants; {segments} aligned segments per variant. Live buffer bytes: 0.");
            return 0;
        }

        private static async Task WritePlaylistsAsync(string directory, IReadOnlyList<HlsVideoRendition> video,
            IReadOnlyList<HlsAudioOutput> audio, int[] variantRates, int[] sourceRates, CancellationToken token)
        {
            long? target = null;
            if (audio.Count > 0)
            {
                target = video.SelectMany(rendition => rendition.Writer.Segments)
                    .Concat(audio.SelectMany(output => output.Writer.Segments))
                    .Max(segment => new MediaTimestamp(segment.Duration.Value, segment.Duration.TimeBase)
                        .Rescale(new(1, 1), TimestampRounding.Ceiling).Value);
            }
            List<HlsVariant> variants = [];
            for (int index = 0; index < video.Count; index++)
            {
                HlsVariant variant = await video[index].WritePlaylistAsync(target, token);
                if (audio.Count > 0)
                {
                    variant = variant with { AudioGroupId = audio[Array.IndexOf(sourceRates, variantRates[index])].GroupId };
                }
                variants.Add(variant);
            }
            List<HlsAudioRendition> groups = [];
            foreach (HlsAudioOutput output in audio)
            {
                groups.Add(await output.WritePlaylistAsync(target!.Value, token));
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "index.m3u8"), HlsMasterPlaylist.Create(variants, groups),
                new UTF8Encoding(false), token);
        }

        private static void ValidateAlignment(IReadOnlyList<HlsVideoRendition> renditions)
        {
            IReadOnlyList<HlsMediaSegment> first = renditions[0].Writer.Segments;
            foreach (HlsVideoRendition rendition in renditions.Skip(1))
            {
                IReadOnlyList<HlsMediaSegment> current = rendition.Writer.Segments;
                if (first.Count != current.Count || first.Where((segment, index) =>
                    segment.Start != current[index].Start || segment.Duration != current[index].Duration).Any())
                {
                    throw new InvalidOperationException("HLS variant segment timelines differ.");
                }
            }
        }
    }
}
