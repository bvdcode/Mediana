// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    /// <summary>Registered PDF page rendering with zero-based page selection and explicit resolution limits.</summary>
    public class PdfImageDecoder(PdfReaderOptions? readerOptions = null, PdfRenderOptions? renderOptions = null) : IImageDecoder
    {
        public string ImplementationId => "pdf.managed.image";
        public int ProbeLength => 5;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix) => prefix.StartsWith("%PDF-"u8);

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            using PdfDocument document = PdfReader.ReadBorrowed(encoded, readerOptions, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.PageIndex, document.PageCount, nameof(request.PageIndex));
            PdfPage page = document.GetPage(request.PageIndex + 1, cancellationToken);
            PixelBuffer pixels = PdfRenderer.Render(page, renderOptions, cancellationToken);
            return ValueTask.FromResult(pixels.ToVideoFrame(allocator, cancellationToken));
        }
    }
}
