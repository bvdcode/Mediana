// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
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
            return await RunAsync(engine, source, outputFile, logger, encoder, request, cancellationToken);
        }

        public static async Task<int> RunAsync(MediaEngine engine, IByteSource source, CommandOutputFile outputFile, ILogger logger,
            IImageEncoder encoder, ImageDecodeRequest? request = null, CancellationToken cancellationToken = default)
        {
            MediaBufferAllocator allocator = CommandImageResources.CreateAllocator();
            Action<string> warning = message => logger.LogWarning("{Warning}", message);
            request = (request ?? new ImageDecodeRequest()) with { WarningHandler = request?.WarningHandler + warning };
            using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator, request, CommandImageResources.OpenOptions, cancellationToken))
            using (VideoFrame srgb = PrepareFrame(frame, encoder, logger, allocator, cancellationToken))
            {
                long encodedLength = 0;
                await outputFile.WriteAsync(async output =>
                {
                    encodedLength = await encoder.EncodeAsync(srgb, output, allocator, cancellationToken);
                }, cancellationToken: cancellationToken);
                await Console.Out.WriteLineAsync($"{encoder.FormatId.ToUpperInvariant()} image: {srgb.Format.Width}x{srgb.Format.Height}; {encodedLength} bytes; sRGB.");
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Image output buffers were not released.");
            }
            return 0;
        }

        private static VideoFrame PrepareFrame(VideoFrame input, IImageEncoder encoder, ILogger logger,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            VideoColorInfo Resolve(VideoFormat format) => PreviewColorPolicy.Resolve(format, logger);
            if ((encoder.Features & ImageEncoderFeatures.SampleAspectRatio) != 0)
            {
                return SrgbConverter.ApplyWithColorPolicy(input, Resolve, allocator, cancellationToken);
            }
            return SrgbPreview.FitWithColorPolicy(input, Resolve, VideoDisplayMatrix.Identity, int.MaxValue, int.MaxValue, allocator, cancellationToken);
        }

        public static Task<int> SvgAsync(string inputPath, CommandOutputFile outputFile, int? width, ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default) =>
            RunAsync(ImageEngineFactory.CreateSvg(width), inputPath, outputFile, logger, encoder, cancellationToken: cancellationToken);

        public static Task<int> TgsAsync(string inputPath, CommandOutputFile outputFile, double seconds, int? width, ILogger logger, IImageEncoder encoder, CancellationToken cancellationToken = default) =>
            RunAsync(ImageEngineFactory.CreateTgs(width), inputPath, outputFile, logger, encoder,
                new ImageDecodeRequest { Time = new MediaTimestamp(TimeSpan.FromSeconds(seconds).Ticks, TimeBase.TimeSpanTicks) }, cancellationToken);
    }
}
