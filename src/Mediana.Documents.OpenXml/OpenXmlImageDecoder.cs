// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Documents.OpenXml
{
    /// <summary>One ZIP signature registration routes OPC documents and models by their declared main-part type, independent of filenames.</summary>
    public class OpenXmlImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly IReadOnlyDictionary<string, IOpenXmlPageDecoder> _decoders;
        private readonly OpenXmlPackageOptions _options;
        private readonly string[] _relationshipTypes;

        public OpenXmlImageDecoder(IEnumerable<IOpenXmlPageDecoder> decoders, OpenXmlPackageOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(decoders);
            Dictionary<string, IOpenXmlPageDecoder> registered = new(StringComparer.Ordinal);
            foreach (IOpenXmlPageDecoder decoder in decoders)
            {
                ArgumentNullException.ThrowIfNull(decoder);
                ArgumentException.ThrowIfNullOrWhiteSpace(decoder.ContentType);
                ArgumentNullException.ThrowIfNull(decoder.RelationshipTypes);
                if (decoder.RelationshipTypes.Count == 0) { throw new ArgumentException("A page decoder must declare its root relationship types.", nameof(decoders)); }
                foreach (string relationship in decoder.RelationshipTypes) { ArgumentException.ThrowIfNullOrWhiteSpace(relationship); }
                if (!registered.TryAdd(decoder.ContentType, decoder)) { throw new ArgumentException("Duplicate Open XML main-part decoder.", nameof(decoders)); }
            }
            _decoders = registered;
            _relationshipTypes = registered.Values.SelectMany(decoder => decoder.RelationshipTypes).Distinct(StringComparer.Ordinal).ToArray();
            if (_relationshipTypes.Length == 0) { throw new ArgumentException("At least one root relationship type is required.", nameof(decoders)); }
            _options = options ?? new();
        }

        public string ImplementationId => "openxml.managed.image";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix) => prefix.StartsWith("PK\x03\x04"u8);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            using OpenXmlPackage package = OpenXmlPackage.Open(encoded, _options, cancellationToken);
            (string main, string rootType) = package.MainPartInfo(_relationshipTypes, cancellationToken);
            return ValueTask.FromResult(Select(package, main, rootType).ReadInfo(package, main, cancellationToken));
        }

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            using OpenXmlPackage package = OpenXmlPackage.Open(encoded, _options, cancellationToken);
            (string main, string rootType) = package.MainPartInfo(_relationshipTypes, cancellationToken);
            return await Select(package, main, rootType).DecodeAsync(package, main, request, allocator, cancellationToken);
        }

        private IOpenXmlPageDecoder Select(OpenXmlPackage package, string main, string rootType)
        {
            string contentType = package.ContentType(main);
            return _decoders.TryGetValue(contentType, out IOpenXmlPageDecoder? decoder)
                && decoder.RelationshipTypes.Contains(rootType, StringComparer.Ordinal) ? decoder
                : throw new NotSupportedException("No registered page decoder supports the Open XML main-part type: " + contentType);
        }
    }
}
