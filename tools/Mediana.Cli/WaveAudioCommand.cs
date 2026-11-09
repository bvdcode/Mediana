// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Wave;
using Mediana.Core.Memory;
using Mediana.IO;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class WaveAudioCommand
    {

        public static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? stream, ILogger logger, CancellationToken cancellationToken)
        {
            MediaEngine engine = AudioEngineFactory.Create();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaBufferAllocator allocator = CommandMediaResources.CreateAllocator();
            MediaProgress progress = new(logger, TimeProvider.System);
            PcmExportResult? result = null;
            await outputFile.WriteAsync(async destination =>
            {
                result = await engine.ExportPcmAsync(source, destination, new WavePcmWriterFactory(), allocator,
                    new() { StreamIndex = stream, Input = CommandMediaResources.OpenOptions }, progress.Report, cancellationToken);
            }, cancellationToken);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Audio conversion buffers were not released."); }
            await Console.Out.WriteLineAsync($"WAVE PCM16: {result!.SamplesPerChannel} samples per channel; {result.Format.SampleRate} Hz; {result.Format.Channels.Count} channels.");
            return 0;
        }
    }
}
