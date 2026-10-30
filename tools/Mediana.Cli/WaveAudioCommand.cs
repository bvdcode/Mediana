// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Wave;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing.Audio;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class WaveAudioCommand
    {
        private const long BufferBudgetBytes = 1024 * 1024;

        public static async Task<int> RunAsync(string input, string output, int? stream, ILogger logger, CancellationToken cancellationToken)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using AudioSession session = stream is int selected
                ? await engine.OpenAudioAsync(source, selected, cancellationToken: cancellationToken)
                : await engine.OpenAudioAsync(source, cancellationToken: cancellationToken);
            MediaProgress progress = new(logger, TimeProvider.System, session.Stream.CodedDuration);
            using AudioFrame first = await session.ReadFrameAsync(cancellationToken)
                ?? throw new InvalidDataException("The input contains no decoded audio samples.");
            AudioFormat format = new(first.Format.SampleRate, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved,
                first.Format.Channels.ToArray());
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            long samples = 0;
            long outputBytes = 0;
            await CommandOutputFile.WriteAsync(output, async destination =>
            {
                await using WavePcmWriter writer = await WavePcmWriter.OpenAsync(destination, format, allocator, cancellationToken);
                await WriteAsync(writer, first, allocator, cancellationToken);
                progress.Report(new MediaDuration(writer.SamplesPerChannel, new TimeBase(1, format.SampleRate)), destination.Position);
                while (true)
                {
                    using AudioFrame? frame = await session.ReadFrameAsync(cancellationToken);
                    if (frame is null)
                    {
                        break;
                    }
                    await WriteAsync(writer, frame, allocator, cancellationToken);
                    progress.Report(new MediaDuration(writer.SamplesPerChannel, new TimeBase(1, format.SampleRate)), destination.Position);
                }
                await writer.CompleteAsync(cancellationToken);
                samples = writer.SamplesPerChannel;
                outputBytes = destination.Position;
            }, cancellationToken);
            progress.Report(new MediaDuration(samples, new TimeBase(1, format.SampleRate)), outputBytes, completed: true);
            first.Dispose();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Audio conversion buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"WAVE PCM16: {samples} samples per channel; {format.SampleRate} Hz; {format.Channels.Count} channels.");
            return 0;
        }

        private static async ValueTask WriteAsync(WavePcmWriter writer, AudioFrame input, MediaBufferAllocator allocator, CancellationToken token)
        {
            using AudioFrame pcm = AudioSampleConverter.Apply(input, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator, token);
            await writer.WriteFrameAsync(pcm, token);
        }
    }
}
