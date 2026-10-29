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
        public static async Task<int> RenderAsync(string input, string output, int pageNumber, int maximumWidth, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input, cancellationToken: cancellationToken);
            PdfPage page = document.GetPage(pageNumber, cancellationToken);
            PixelBuffer image = PdfRenderer.Render(page, new() { MaximumWidth = maximumWidth }, cancellationToken: cancellationToken);
            MediaBufferAllocator allocator = new(256L * 1024 * 1024);
            using VideoFrame frame = image.ToVideoFrame(allocator, cancellationToken);
            await WriteAsync(frame, output, allocator, cancellationToken);
            await Console.Out.WriteLineAsync($"PDF page {pageNumber}/{document.PageCount}: {page.Width} x {page.Height} pt; {image.Width} x {image.Height} px.");
            return 0;
        }
        public static async Task<int> ImageAsync(string input, string output, int pageNumber, int index, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input, cancellationToken: cancellationToken);
            PdfPage page = document.GetPage(pageNumber, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, page.Images.Count);
            MediaBufferAllocator allocator = new(256L * 1024 * 1024);
            using VideoFrame frame = page.Images[index].ToVideoFrame(allocator, cancellationToken);
            await WriteAsync(frame, output, allocator, cancellationToken);
            return 0;
        }
        private static async Task WriteAsync(VideoFrame frame, string path, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            using MediaBuffer png = PngEncoder.Encode(frame, allocator, cancellationToken: cancellationToken);
            await CommandOutputFile.WriteAsync(path, async stream => await stream.WriteAsync(png.Memory, cancellationToken), cancellationToken: cancellationToken);
        }
    }
}
