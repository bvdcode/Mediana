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
            await using MonoAudioReader reader = new(session.ReadFrameAsync, gains, sampleRate, allocator, AudioSampleFormat.Signed16);
            PcmStreamWriteResult? result = null;
            await outputFile.WriteAsync(async output =>
            {
                result = await PcmStreamWriter.WriteFramesAsync(reader.ReadFrameAsync, output, allocator, PcmByteOrder.LittleEndian, cancellationToken);
            }, cancellationToken);
            await reader.DisposeAsync();
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0) { throw new InvalidOperationException("Audio processing buffers were not released."); }
            await Console.Out.WriteLineAsync($"Mono PCM16LE: {result!.SamplesPerChannel} samples; {sampleRate} Hz. Live buffer bytes: 0.");
            return 0;
        }
    }
}
