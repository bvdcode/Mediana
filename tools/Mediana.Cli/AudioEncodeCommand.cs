// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Aac;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class AudioEncodeCommand
    {

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? streamIndex, int bitRate, int? channels, ILogger logger, CancellationToken token)
        {
            MediaEngine engine = AudioEngineFactory.CreateBuilder().AddAudioEncoder(new AacEncoderFactory(new() { BitRate = bitRate }))
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaBufferAllocator allocator = CommandMediaResources.CreateAllocator();
            MediaProgress progress = new(logger, TimeProvider.System);
            AudioTranscodeResult? result = null;
            await outputFile.WriteAsync(async destination =>
            {
                result = await engine.TranscodeAudioAsync(source, destination, "aac", "mpegts", allocator,
                    new() { StreamIndex = streamIndex, Channels = channels, Input = CommandMediaResources.OpenOptions }, progress.Report, token);
            }, token);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("AAC encoding buffers were not released."); }
            await Console.Out.WriteLineAsync($"AAC-LC MPEG-TS: {result!.SamplesPerChannel} input samples per channel; {result.Encoding.Packets} packets; priming {result.PrimingSamples}; padding {result.PaddingSamples}. Live buffer bytes: 0.");
            return 0;
        }
    }
}
