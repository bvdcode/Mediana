// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Metadata;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.Documents.Pdf
{
    /// <summary>Registered PDF page rendering with zero-based page selection and explicit resolution limits.</summary>
    public class PdfImageDecoder(PdfReaderOptions? readerOptions = null, PdfRenderOptions? renderOptions = null) : IImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "pdf.managed.image";
        public int ProbeLength => 32;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("%PDF-"u8) || CmsPdfContent.IsSignedDataPrefix(prefix);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            using PdfDocument document = PdfReader.ReadBorrowed(encoded, readerOptions, cancellationToken);
            if (document.PageCount == 0)
            {
                throw new InvalidDataException("PDF contains no pages.");
            }
            PdfPageInfo page = document.GetPageInfo(1);
            List<MediaMetadata> pages = [];
            for (int number = 1; number <= document.PageCount; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PdfPageInfo current = document.GetPageInfo(number);
                pages.Add(new("page/" + number, [new("pdf", "width_points", current.Width), new("pdf", "height_points", current.Height),
                    new("pdf", "rotation_degrees", current.Rotation)]));
            }
            return ValueTask.FromResult(new ImageInfo("pdf", page.Width, page.Height, ImageDimensionUnit.Points, document.PageCount,
                metadata: new("document", [new("pdf", "page_count", document.PageCount)], pages)));
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            using PdfDocument document = PdfReader.ReadBorrowed(encoded, readerOptions, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.PageIndex, document.PageCount, nameof(request.PageIndex));
            foreach (string warning in document.Warnings)
            {
                request.WarningHandler?.Invoke(warning);
            }
            PdfRenderOptions rendering = (renderOptions ?? new PdfRenderOptions()) with
            {
                WarningHandler = renderOptions?.WarningHandler + request.WarningHandler
            };
            using PixelBuffer pixels = PdfRenderer.Render(document, request.PageIndex + 1, rendering, cancellationToken, allocator);
            return ValueTask.FromResult(pixels.PublishVideoFrame(cancellationToken));
        }
    }
}
