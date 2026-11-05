// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Documents.OpenXml
{
    /// <summary>Decodes a document selected by its OPC main-part content type. The package is borrowed only during the call.</summary>
    public interface IOpenXmlPageDecoder
    {
        string ContentType { get; }
        IReadOnlyList<string> RelationshipTypes { get; }
        ImageInfo ReadInfo(OpenXmlPackage package, string mainPart, CancellationToken cancellationToken = default);
        ValueTask<VideoFrame> DecodeAsync(OpenXmlPackage package, string mainPart, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default);
    }
}
