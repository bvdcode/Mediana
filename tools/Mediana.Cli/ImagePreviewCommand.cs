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
    internal static class ImagePreviewCommand
    {
        public static async Task<int> RunAsync(IImageDecoder decoder, string inputPath, CommandOutputFile outputFile, ILogger logger,
            IImageEncoder encoder, ImageDecodeRequest? request = null, CancellationToken cancellationToken = default)
        {
            MediaEngine engine = ImageEngineFactory.AddDecoders(new MediaEngineBuilder()).ReplaceImageDecoder(decoder).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(inputPath));
            return await RunAsync(engine, source, outputFile, logger, encoder, request, cancellationToken, new()
            {
                Images = CommandImageResources.OpenOptions with { DecoderImplementationId = decoder.ImplementationId },
                Request = request ?? new()
            });
        }

        public static async Task<int> RunAsync(MediaEngine engine, IByteSource source, CommandOutputFile outputFile, ILogger logger,
            IImageEncoder encoder, ImageDecodeRequest? request = null, CancellationToken cancellationToken = default,
            VisualOpenOptions? input = null, int maximumWidth = int.MaxValue, int maximumHeight = int.MaxValue)
        {
            MediaBufferAllocator allocator = CommandImageResources.CreateAllocator();
            VisualExportOptions options = new()
            {
                Input = input ?? new() { Images = CommandImageResources.OpenOptions, Request = request ?? new() },
                MaximumWidth = maximumWidth, MaximumHeight = maximumHeight,
                ColorResolver = SdrPreviewColorPolicy.Resolve,
                WarningHandler = message => logger.LogWarning("{Warning}", message)
            };
            VisualExportResult? result = null;
            await outputFile.WriteAsync(async output =>
            {
                result = await engine.ExportPreviewAsync(source, output, encoder, allocator, options, cancellationToken);
            }, cancellationToken);
            if (allocator.AllocatedBytes != 0) { throw new InvalidOperationException("Image output buffers were not released."); }
            string timing = result!.FirstTimestamp is MediaTimestamp timestamp
                ? $"; PTS {timestamp.Value} at {timestamp.TimeBase.Numerator}/{timestamp.TimeBase.Denominator}" : string.Empty;
            await Console.Out.WriteLineAsync($"{encoder.FormatId.ToUpperInvariant()} preview: {result.Width}x{result.Height}; {result.BytesWritten} bytes{timing}; sRGB. Live buffer bytes: 0.");
            return 0;
        }

        public static Task<int> SvgAsync(string inputPath, CommandOutputFile outputFile, int? width, ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default) =>
            RunAsync(ImageEngineFactory.CreateSvg(width), inputPath, outputFile, logger, encoder, cancellationToken: cancellationToken);

        public static Task<int> TgsAsync(string inputPath, CommandOutputFile outputFile, double seconds, int? width, ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default) =>
            RunAsync(ImageEngineFactory.CreateTgs(width), inputPath, outputFile, logger, encoder,
                new ImageDecodeRequest { Time = new MediaTimestamp(TimeSpan.FromSeconds(seconds).Ticks, TimeBase.TimeSpanTicks) }, cancellationToken);
    }
}
