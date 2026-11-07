// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
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
            MediaEngine engine = ContainerEngineFactory.CreateVideoBuilder(Mp4TimestampMode.LinearMovie).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using VideoSession session = await engine.OpenVideoAsync(source, videoIndex, cancellationToken: token);
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            int[] sourceRates = separateAudio ? audioBitRates.Distinct().ToArray() : audioBitRates;
            await using HlsAudioSource? audio = audioIndex is int index ? audioCodec switch
            {
                null => await HlsCopyAudioSource.OpenAsync(source, index, allocator, token),
                AudioCodecMode.Aac => await HlsAacAudioSource.OpenAsync(AudioEngineFactory.Create(), source, index, sourceRates, audioChannels, allocator, token),
                _ => throw new ArgumentOutOfRangeException(nameof(audioCodec))
            } : null;
            HlsRenditionOptions options = new()
            {
                VideoProfiles = widths.Select((width, index) => new HlsVideoProfile(width,
                    new H264EncoderOptions { Quantizer = qp, RateControl = videoRates?[index] })).ToArray(),
                TargetDuration = new(targetMilliseconds, new(1, 1000)),
                SeparateAudio = separateAudio,
                AudioProfileIndices = separateAudio ? audioBitRates.Select(rate => Array.IndexOf(sourceRates, rate)).ToArray() : null
            };
            MediaProgress progress = new(logger, TimeProvider.System, session.Stream.CodedDuration);
            HlsEncodingResult? result = null;
            await CommandOutputDirectory.WriteAsync(output, async directory =>
            {
                result = await HlsRenditionEncoder.EncodeAsync(session, new HlsDirectoryOutput(directory), options, allocator, audio,
                    update => progress.Report(update.ProcessedDuration, update.BytesWritten, update.Completed), token);
                if (audio is not null) { await audio.DisposeAsync(); }
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
            await console.WriteLineAsync($"HLS encoding: {result!.InputFrames} input frames; {result.AudioPackets} audio packets; {result.VideoRenditions} variants; {result.SegmentsPerRendition} aligned segments per variant. Live buffer bytes: 0.");
            return 0;
        }

    }
}
