// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Drawing;

namespace Mediana.Models.Mesh
{
    /// <summary>Renders a 3MF root build through the shared OPC content-type registration.</summary>
    public class ThreeMfPageDecoder(ThreeMfReaderOptions? readerOptions = null, MeshRenderOptions? renderOptions = null) : IOpenXmlPageDecoder
    {
        private static readonly IReadOnlyList<string> Relationships = Array.AsReadOnly(new[] { ThreeMfPackage.ModelRelationship });
        public string ContentType => ThreeMfReader.ModelContentType;
        public IReadOnlyList<string> RelationshipTypes => Relationships;

        public ImageInfo ReadInfo(OpenXmlPackage package, string mainPart, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeshRenderOptions options = renderOptions ?? new();
            options.Validate();
            if (package.ReadXml(mainPart, cancellationToken).Root?.Name != ThreeMfReader.Core + "model")
            {
                throw new InvalidDataException("Invalid 3MF model root.");
            }
            return new("3mf", options.Width, options.Height, ImageDimensionUnit.Pixels, isPreviewCanvas: true);
        }

        public async ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package, string mainPart, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(ImageDecoderFeatures.SingleImage);
            ThreeMfDocument document = await ThreeMfReader.ReadAsync(package, readerOptions, cancellationToken).ConfigureAwait(false);
            TriangleMesh mesh = document.CreateBuildMesh(cancellationToken);
            using PixelBuffer pixels = MeshRenderer.Render(mesh, renderOptions, cancellationToken, allocator);
            return pixels.PublishVideoFrame(cancellationToken);
        }
    }
}
