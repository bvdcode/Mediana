// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Word;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal class WordPreviewDecoder : IOpenXmlPageDecoder
    {
        private static readonly WordReaderOptions ReaderOptions = new() { Package = CommandImageResources.PackageOptions, AllowFractionalMeasurements = true, DefaultPageSize = WordPageSize.A4 };
        private static readonly WordRenderOptions RenderOptions = new() { MaximumWidth = 1200, MaximumHeight = 1200, MaximumSamples = CommandImageResources.MaximumRgbaPixels, MaximumImagePixels = CommandImageResources.MaximumRgbaPixels, ImageColorResolver = SdrPreviewColorPolicy.Resolve };

        public string ContentType => WordReader.DocumentContentType;
        public IReadOnlyList<string> RelationshipTypes => OpenXmlPackage.OfficeDocumentRelationships;

        public ImageInfo ReadInfo(OpenXmlPackage package, string mainPart, CancellationToken cancellationToken = default) =>
            new WordPageDecoder(ReaderOptions).ReadInfo(package, mainPart, cancellationToken);

        public async ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package, string mainPart, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            WordLayoutOptions layout = new() { Text = await CommandTextResources.GetAsync(cancellationToken) };
            return await new WordPageDecoder(ReaderOptions, layout, RenderOptions).DecodeAsync(package, mainPart, request, allocator, cancellationToken);
        }
    }
}
