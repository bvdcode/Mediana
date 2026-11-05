// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Drawing;

namespace Mediana.Documents.Word
{
    /// <summary>Adapts Word pagination and direct rasterization to the shared Open XML image registration.</summary>
    public class WordPageDecoder(WordReaderOptions? readerOptions = null, WordLayoutOptions? layoutOptions = null,
        WordRenderOptions? renderOptions = null) : IOpenXmlPageDecoder
    {
        public string ContentType => WordReader.DocumentContentType;
        public IReadOnlyList<string> RelationshipTypes => OpenXmlPackage.OfficeDocumentRelationships;

        public ImageInfo ReadInfo(OpenXmlPackage package, string mainPart, CancellationToken cancellationToken = default)
        {
            WordPageSize size = WordReader.ReadPageSize(package, mainPart, readerOptions ?? new(), cancellationToken);
            return new("docx", size.Width, size.Height, ImageDimensionUnit.Points);
        }

        public async ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package, string mainPart, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(ImageDecoderFeatures.Pages);
            WordDocument document = WordReader.Read(package, mainPart, readerOptions ?? new(), cancellationToken);
            WordLayout layout = WordRenderer.Layout(document, layoutOptions, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.PageIndex, layout.PageCount, nameof(request.PageIndex));
            WordRenderOptions rendering = (renderOptions ?? new()) with { WarningHandler = renderOptions?.WarningHandler + request.WarningHandler };
            using PixelBuffer pixels = await WordRenderer.RenderAsync(layout, request.PageIndex + 1, rendering, cancellationToken, allocator);
            return pixels.PublishVideoFrame(cancellationToken);
        }
    }
}
