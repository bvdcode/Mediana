// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Cli
{
    internal static class AudioDecodeCommand
    {
        public static async Task<int> RunAsync(string inputPath, CommandOutputFile outputFile, int streamIndex, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using AudioSession session = await engine.OpenAudioAsync(source, streamIndex, cancellationToken: cancellationToken);
            MediaBufferAllocator allocator = new(long.MaxValue);
            PcmStreamWriteResult? result = null;
            await outputFile.WriteAsync(async output =>
            {
                result = await PcmStreamWriter.WriteFramesAsync(session.ReadFrameAsync, output, allocator, cancellationToken: cancellationToken);
            }, cancellationToken);
            await session.DisposeAsync();
            if (allocator.AllocatedBytes != 0 || session.AllocatedBufferBytes != 0) { throw new InvalidOperationException("Audio buffers were not released."); }
            if (result!.Format is not AudioFormat format)
            {
                await Console.Out.WriteLineAsync("PCM output: no samples. Live buffer bytes: 0.");
                return 0;
            }
            await Console.Out.WriteLineAsync($"PCM output: {result.SamplesPerChannel} samples per channel; {format.SampleRate} Hz; {format.SampleFormat}; "
                + $"{string.Join(", ", format.Channels)}; native-endian interleaved. Live buffer bytes: 0.");
            return 0;
        }
    }
}
