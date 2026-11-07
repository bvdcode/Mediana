// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.IO;
using Mediana.Processing;
using Microsoft.Extensions.Logging;

namespace Mediana.Cli
{
    internal static class ImageSequenceCommand
    {
        internal static async Task<int> RunAsync(string input, CommandOutputFile outputFile, int? stream, long milliseconds,
            ILogger logger, TextWriter writer, IImageSequenceEncoder encoder, CancellationToken token)
        {
            MediaEngine engine = ImageEngineFactory.CreateVisual();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(input));
            MediaBufferAllocator allocator = CommandImageResources.CreateAllocator();
            VisualExportOptions options = new()
            {
                Input = new()
                {
                    StreamIndex = stream, Images = CommandImageResources.OpenOptions,
                    Request = new() { Time = new MediaTimestamp(milliseconds, new(1, 1000)) }
                },
                ColorResolver = SdrPreviewColorPolicy.Resolve,
                WarningHandler = message => logger.LogWarning("{Warning}", message)
            };
            MediaProgress progress = new(logger, TimeProvider.System);
            VisualExportResult? result = null;
            await outputFile.WriteAsync(async output =>
            {
                result = await engine.ExportAnimationAsync(source, output, encoder, allocator, options, progress.Report, token);
            }, token);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Animation output buffers were not released."); }
            await writer.WriteLineAsync($"{encoder.FormatId.ToUpperInvariant()} animation: {result!.Frames} frames; {result.BytesWritten} bytes; sRGB. Live buffer bytes: 0.");
            return 0;
        }
    }
}
