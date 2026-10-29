// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;
namespace Mediana.Sample
{
    internal static class PdfPreviewSample
    {
        public static async Task<int> RenderAsync(string input, string output, int pageNumber, int maximumWidth)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input);
            PdfPage page = document.GetPage(pageNumber);
            PixelBuffer image = PdfRenderer.Render(page, new() { MaximumWidth = maximumWidth });
            MediaBufferAllocator allocator = new(256L * 1024 * 1024);
            using VideoFrame frame = image.ToVideoFrame(allocator);
            await WriteAsync(frame, output, allocator);
            await Console.Out.WriteLineAsync($"PDF page {pageNumber}/{document.PageCount}: {page.Width} x {page.Height} pt; {image.Width} x {image.Height} px.");
            return 0;
        }
        public static async Task<int> ImageAsync(string input, string output, int pageNumber, int index)
        {
            using PdfDocument document = await PdfReader.ReadFileAsync(input);
            PdfPage page = document.GetPage(pageNumber);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, page.Images.Count);
            MediaBufferAllocator allocator = new(256L * 1024 * 1024);
            using VideoFrame frame = page.Images[index].ToVideoFrame(allocator);
            await WriteAsync(frame, output, allocator);
            return 0;
        }
        private static async Task WriteAsync(VideoFrame frame, string path, MediaBufferAllocator allocator)
        {
            using MediaBuffer png = PngEncoder.Encode(frame, allocator);
            await SampleOutputFile.WriteAsync(path, async stream => await stream.WriteAsync(png.Memory));
        }
    }
}
