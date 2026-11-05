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
            return new("docx", size.Width, size.Height, ImageDimensionUnit.Points,
                metadata: new("document", [new("ooxml", "main_part", mainPart), new("ooxml", "content_type", ContentType),
                    new("word", "first_section_width_points", size.Width), new("word", "first_section_height_points", size.Height)]));
        }

        public async ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package, string mainPart, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(ImageDecoderFeatures.Pages);
            WordDocument document = WordReader.Read(package, mainPart, readerOptions ?? new(), cancellationToken);
            WordLayoutOptions layoutSettings = layoutOptions ?? new();
            if (request.Resources is not null && await request.Resources.GetAsync<TextOptions>(cancellationToken).ConfigureAwait(false) is TextOptions text)
            {
                layoutSettings = layoutSettings with { Text = text };
            }
            WordLayout layout = WordRenderer.Layout(document, layoutSettings, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.PageIndex, layout.PageCount, nameof(request.PageIndex));
            WordRenderOptions rendering = (renderOptions ?? new()) with
            {
                Images = request.Images ?? renderOptions?.Images ?? Imaging.ImageResources.DefaultDecoders,
                WarningHandler = renderOptions?.WarningHandler + request.WarningHandler
            };
            using PixelBuffer pixels = await WordRenderer.RenderAsync(layout, request.PageIndex + 1, rendering, cancellationToken, allocator);
            return pixels.PublishVideoFrame(cancellationToken);
        }
    }
}
