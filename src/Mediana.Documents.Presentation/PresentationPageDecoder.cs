// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Drawing;
namespace Mediana.Documents.Presentation
{
    public class PresentationPageDecoder(PresentationReaderOptions? readerOptions = null,PresentationRenderOptions? renderOptions = null) : IOpenXmlPageDecoder
    {
        public string ContentType => PresentationReader.DocumentContentType;
        public IReadOnlyList<string> RelationshipTypes => OpenXmlPackage.OfficeDocumentRelationships;
        public ImageInfo ReadInfo(OpenXmlPackage package,string mainPart,CancellationToken cancellationToken = default)
        {
            using PresentationDocument document = new(package,mainPart,readerOptions ?? new(),false,cancellationToken);
            return new("pptx",document.Width,document.Height,ImageDimensionUnit.Points,pageCount:document.Slides.Count,
                metadata:new("document",[new("ooxml","main_part",mainPart),new("ooxml","content_type",ContentType)]));
        }
        public async ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package,string mainPart,ImageDecodeRequest request,MediaBufferAllocator allocator,CancellationToken cancellationToken = default)
        {
            request.Validate(ImageDecoderFeatures.Pages);
            using PresentationDocument document = new(package,mainPart,readerOptions ?? new(),false,cancellationToken);
            PresentationRenderOptions rendering = renderOptions ?? new();
            TextOptions? text = request.Resources is null ? null : await request.Resources.GetAsync<TextOptions>(cancellationToken).ConfigureAwait(false);
            rendering = rendering with { Text = text ?? rendering.Text,Images = request.Images ?? rendering.Images,WarningHandler = rendering.WarningHandler + request.WarningHandler };
            using PixelBuffer pixels = await PresentationRenderer.RenderAsync(document,request.PageIndex,rendering,cancellationToken,allocator).ConfigureAwait(false);
            return pixels.PublishVideoFrame(cancellationToken);
        }
    }
}
