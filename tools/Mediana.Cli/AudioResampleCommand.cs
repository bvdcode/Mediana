// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;

namespace Mediana.Cli
{
    internal static class AudioResampleCommand
    {
        private const int BufferBudgetBytes = 1024 * 1024;

        public static async Task<int> RunAsync(string inputPath, string outputPath, int streamIndex, int sampleRate, double[] gains)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            await using AudioSession session = await engine.OpenAudioAsync(source, streamIndex,
                new MediaOpenOptions { BufferBudgetBytes = BufferBudgetBytes });
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
            await CommandOutputFile.WriteAsync(outputPath, async output =>
            {
                while (true)
                {
                    using AudioFrame? frame = await resampler.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    using AudioFrame pcm = AudioSampleConverter.Apply(frame, AudioSampleFormat.Signed16, AudioStorageLayout.Interleaved, allocator);
                    using WritableMediaBuffer littleEndian = allocator.Allocate(pcm.GetPlane(0).Length);
                    ReadOnlySpan<short> values = MemoryMarshal.Cast<byte, short>(pcm.GetPlane(0).Span);
                    for (int index = 0; index < values.Length; index++)
                    {
                        BinaryPrimitives.WriteInt16LittleEndian(littleEndian.Memory.Span[(index * sizeof(short))..], values[index]);
                    }
                    await output.WriteAsync(littleEndian.Memory);
                    samples = checked(samples + frame.SamplesPerChannel);
                }
            });
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
