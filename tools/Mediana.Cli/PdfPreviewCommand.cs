// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;
namespace Mediana.Cli
{
    internal static class PdfPreviewCommand
    {
        public static async Task<int> RenderAsync(string input, CommandOutputFile output, int pageNumber, int maximumWidth, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input, cancellationToken: cancellationToken);
            PdfPageInfo page = document.GetPageInfo(pageNumber);
            MediaBufferAllocator allocator = new(256L * 1024 * 1024);
            using PixelBuffer image = PdfRenderer.Render(document, pageNumber, new() { MaximumWidth = maximumWidth }, cancellationToken, allocator);
            using VideoFrame frame = image.PublishVideoFrame(cancellationToken);
            await WriteAsync(frame, output, allocator, cancellationToken);
            await Console.Out.WriteLineAsync($"PDF page {pageNumber}/{document.PageCount}: {page.Width} x {page.Height} pt; {image.Width} x {image.Height} px.");
            return 0;
        }
        public static async Task<int> ImageAsync(string input, CommandOutputFile output, int pageNumber, int index, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input, cancellationToken: cancellationToken);
            using PdfPage page = document.GetPage(pageNumber, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, page.Images.Count);
            MediaBufferAllocator allocator = new(256L * 1024 * 1024);
            using VideoFrame frame = page.Images[index].ToVideoFrame(allocator, cancellationToken);
            await WriteAsync(frame, output, allocator, cancellationToken);
            return 0;
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
