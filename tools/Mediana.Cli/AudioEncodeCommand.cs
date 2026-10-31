// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Execution;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class AudioEncodeCommand
    {
        private const long BufferBudgetBytes = 16 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? streamIndex, int bitRate, int? channels, ILogger logger, CancellationToken token)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using AudioSession session = streamIndex is int index ? await engine.OpenAudioAsync(source, index, cancellationToken: token) :
                await engine.OpenAudioAsync(source, cancellationToken: token);
            using AudioFrame first = await session.ReadFrameAsync(token) ?? throw new InvalidDataException("The input contains no decoded audio samples.");
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            AudioEncodingPcm converter = new(first.Format, channels);
            using AudioFrame prepared = converter.Apply(first, allocator, token);
            using AacEncoder encoder = AacEncoder.Create(prepared.Format, new(1, prepared.Format.SampleRate), allocator, new() { BitRate = bitRate });
            MediaProgress progress = new(logger, TimeProvider.System, session.Stream.CodedDuration);
            long samples = 0;
            long packets = 0;
            await outputFile.WriteAsync(async destination =>
            {
                await using MpegTsWriter writer = MpegTsWriter.Create(destination, [encoder.Stream], allocator);
                bool opening = true;
                MediaOperationProgress result = await MediaOperations.EncodeAudioAsync(ReadAsync, encoder, writer, update =>
                {
                    if (update.ProcessedDuration is MediaDuration duration)
                    {
                        samples = duration.Value;
                        progress.Report(duration, writer.BytesWritten, update.IsComplete);
                    }
                }, token);
                packets = result.Packets;

                async ValueTask<AudioFrame?> ReadAsync(CancellationToken cancellationToken)
                {
                    if (opening)
                    {
                        opening = false;
                        AudioFrame result = prepared.Retain();
                        prepared.Dispose();
                        first.Dispose();
                        return result;
                    }
                    using AudioFrame? frame = await session.ReadFrameAsync(cancellationToken);
                    return frame is null ? null : converter.Apply(frame, allocator, cancellationToken);
                }
            }, token);
            int? padding = encoder.PaddingSamples;
            encoder.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("AAC encoding buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"AAC-LC MPEG-TS: {samples} input samples per channel; {packets} packets; priming {encoder.PrimingSamples}; padding {padding}. Live buffer bytes: 0.");
            return 0;
        }
    }
}
