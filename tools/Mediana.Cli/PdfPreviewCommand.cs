// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;
using Microsoft.Extensions.Logging;
namespace Mediana.Cli
{
    internal static class PdfPreviewCommand
    {
        public static async Task<int> RenderAsync(string input, CommandOutputFile output, int pageNumber, int maximumWidth, ILogger logger, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input, CommandImageResources.PdfReaderOptions, cancellationToken);
            LogWarnings(document.Warnings, logger);
            PdfPageInfo page = document.GetPageInfo(pageNumber);
            MediaBufferAllocator allocator = CommandImageResources.CreateAllocator();
            using PixelBuffer image = PdfRenderer.Render(document, pageNumber, CommandImageResources.PdfRenderOptions with { MaximumWidth = maximumWidth, WarningHandler = message => logger.LogWarning("{Warning}", message) }, cancellationToken, allocator);
            using VideoFrame frame = image.PublishVideoFrame(cancellationToken);
            await WriteAsync(frame, output, allocator, cancellationToken);
            await Console.Out.WriteLineAsync($"PDF page {pageNumber}/{document.PageCount}: {page.Width} x {page.Height} pt; {image.Width} x {image.Height} px.");
            return 0;
        }
        public static async Task<int> ImageAsync(string input, CommandOutputFile output, int pageNumber, int index, ILogger logger, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input, CommandImageResources.PdfReaderOptions, cancellationToken);
            LogWarnings(document.Warnings, logger);
            using PdfPage page = document.GetPage(pageNumber, cancellationToken);
            LogWarnings(page.Warnings, logger);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, page.Images.Count);
            MediaBufferAllocator allocator = CommandImageResources.CreateAllocator();
            using VideoFrame frame = page.Images[index].ToVideoFrame(allocator, cancellationToken);
            await WriteAsync(frame, output, allocator, cancellationToken);
            return 0;
        }
        private static void LogWarnings(IReadOnlyList<string> warnings, ILogger logger)
        {
            foreach (string warning in warnings)
            {
                logger.LogWarning("{Warning}", warning);
            }
        }

        private static async Task WriteAsync(VideoFrame frame, CommandOutputFile output, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            await output.WriteAsync(async stream =>
            {
                await PngEncoder.EncodeAsync(frame, stream, allocator, cancellationToken);
            }, cancellationToken: cancellationToken);
        }
    }
}
