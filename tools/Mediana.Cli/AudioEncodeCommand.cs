// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.IO;
using Mediana.Processing.Audio;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class AudioEncodeCommand
    {
        private const long BufferBudgetBytes = 16 * 1024 * 1024;

        public static async Task<int> RunAsync(string input, string output, int? streamIndex, int bitRate, ILogger logger, CancellationToken token)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            await using AudioSession session = streamIndex is int index ? await engine.OpenAudioAsync(source, index, cancellationToken: token) :
                await engine.OpenAudioAsync(source, cancellationToken: token);
            using AudioFrame first = await session.ReadFrameAsync(token) ?? throw new InvalidDataException("The input contains no decoded audio samples.");
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using AudioFrame prepared = AudioSampleConverter.Apply(first, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator, token);
            using AacEncoder encoder = AacEncoder.Create(prepared.Format, new(1, prepared.Format.SampleRate), allocator, new() { BitRate = bitRate });
            AudioProgress progress = new(logger, TimeProvider.System);
            long samples = 0;
            long packets = 0;
            await CommandOutputFile.WriteAsync(output, async destination =>
            {
                await using MpegTsWriter writer = MpegTsWriter.Create(destination, [encoder.Stream], allocator);
                await WriteAsync(prepared);
                prepared.Dispose();
                first.Dispose();
                while (await session.ReadFrameAsync(token) is AudioFrame frame)
                {
                    using (frame)
                    using (AudioFrame converted = AudioSampleConverter.Apply(frame, AudioSampleFormat.Float32, AudioStorageLayout.Interleaved, allocator, token))
                    {
                        await WriteAsync(converted);
                    }
                }
                encoder.CompleteInput();
                await DrainAsync();
                await writer.CompleteAsync(token);
                progress.Report(samples, prepared.Format.SampleRate, writer.BytesWritten, completed: true);

                async ValueTask WriteAsync(AudioFrame frame)
                {
                    if (encoder.TrySend(frame, token) != CodecInputStatus.Accepted)
                    {
                        throw new InvalidOperationException("The AAC encoder did not accept its next PCM block.");
                    }
                    samples = checked(samples + frame.SamplesPerChannel);
                    await DrainAsync();
                    progress.Report(samples, prepared.Format.SampleRate, writer.BytesWritten);
                }

                async ValueTask DrainAsync()
                {
                    while (true)
                    {
                        CodecOutput<MediaPacket> result = encoder.Receive(token);
                        if (result.Status != CodecOutputStatus.Available)
                        {
                            return;
                        }
                        using MediaPacket packet = result.Value;
                        await writer.WritePacketAsync(packet, token);
                        packets++;
                    }
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
