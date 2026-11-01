// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Cli
{
    internal static class AudioResampleCommand
    {
        private const int BufferBudgetBytes = 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int streamIndex, int sampleRate, double[] gains, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using AudioSession session = await engine.OpenAudioAsync(source, streamIndex,
                new MediaOpenOptions { BufferBudgetBytes = BufferBudgetBytes }, cancellationToken: cancellationToken);
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            AudioChannelMixer? mixer = null;
            async ValueTask<AudioFrame?> ReadMixedAsync(CancellationToken cancellationToken)
            {
                using AudioFrame? input = await session.ReadFrameAsync(cancellationToken);
                if (input is null)
                {
                    return null;
                }
                mixer ??= new AudioChannelMixer(input.Format, ["mono"], gains);
                return mixer.Apply(input, allocator, cancellationToken);
            }
            await using AudioResampler resampler = new(ReadMixedAsync, sampleRate, allocator);
            long samples = 0;
            await outputFile.WriteAsync(async output =>
            {
                while (true)
                {
                    using AudioFrame? frame = await resampler.ReadFrameAsync(cancellationToken);
                    if (frame is null)
                    {
                        break;
                    }
                    using AudioFrame pcm = AudioSampleConverter.Apply(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator, cancellationToken: cancellationToken);
                    await PcmStreamWriter.WriteLittleEndianAsync(output, pcm, allocator, cancellationToken);
                    samples = checked(samples + frame.SamplesPerChannel);
                }
            }, cancellationToken: cancellationToken);
            await resampler.DisposeAsync();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0)
            {
                throw new InvalidOperationException("Audio processing buffers were not released.");
            }
            await Console.Out.WriteLineAsync($"Mono PCM16LE: {samples} samples; {sampleRate} Hz. Live buffer bytes: 0.");
            return 0;
        }
    }
}
