// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Documents.OpenXml
{
    /// <summary>One ZIP signature registration routes Office documents by their declared main-part type, independent of filenames.</summary>
    public class OpenXmlImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly IReadOnlyDictionary<string, IOpenXmlPageDecoder> _decoders;
        private readonly OpenXmlPackageOptions _options;

        public OpenXmlImageDecoder(IEnumerable<IOpenXmlPageDecoder> decoders, OpenXmlPackageOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(decoders);
            Dictionary<string, IOpenXmlPageDecoder> registered = new(StringComparer.Ordinal);
            foreach (IOpenXmlPageDecoder decoder in decoders)
            {
                ArgumentNullException.ThrowIfNull(decoder);
                ArgumentException.ThrowIfNullOrWhiteSpace(decoder.ContentType);
                if (!registered.TryAdd(decoder.ContentType, decoder)) { throw new ArgumentException("Duplicate Open XML main-part decoder.", nameof(decoders)); }
            }
            _decoders = registered;
            _options = options ?? new();
        }

        public string ImplementationId => "openxml.managed.image";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix) => prefix.StartsWith("PK\x03\x04"u8);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            using OpenXmlPackage package = OpenXmlPackage.Open(encoded, _options, cancellationToken);
            string main = package.MainPart(cancellationToken);
            return ValueTask.FromResult(Select(package, main).ReadInfo(package, main, cancellationToken));
        }

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            using OpenXmlPackage package = OpenXmlPackage.Open(encoded, _options, cancellationToken);
            string main = package.MainPart(cancellationToken);
            return await Select(package, main).DecodeAsync(package, main, request, allocator, cancellationToken);
        }

        private IOpenXmlPageDecoder Select(OpenXmlPackage package, string main)
        {
            string contentType = package.ContentType(main);
            return _decoders.TryGetValue(contentType, out IOpenXmlPageDecoder? decoder) ? decoder
                : throw new NotSupportedException("No registered page decoder supports the Open XML main-part type: " + contentType);
        }
    }
}
