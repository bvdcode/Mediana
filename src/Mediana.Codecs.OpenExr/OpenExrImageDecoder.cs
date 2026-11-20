// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.OpenExr
{
    /// <summary>Decodes selected OpenEXR scanline parts to premultiplied float32 RGBA, preserving scene-linear HDR samples.</summary>
    public class OpenExrImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly OpenExrDecodingOptions options;
        public OpenExrImageDecoder(OpenExrDecodingOptions? options = null)
        {
            this.options = options ?? new();
            ArgumentNullException.ThrowIfNull(this.options.Layer);
            if (this.options.Layer.Contains('\0')) { throw new ArgumentException("OpenEXR layer names cannot contain null characters.", nameof(options)); }
        }
        public string ImplementationId => "openexr.managed.image";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 4
            && BinaryPrimitives.ReadUInt32LittleEndian(prefix) == ExrDocument.Magic;
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            ExrDocument document = new(encoded, cancellationToken);
            return ValueTask.FromResult(ExrImageMetadata.Read(document, cancellationToken));
        }
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            ExrDocument document = new(encoded, cancellationToken);
            if (request.PageIndex >= document.Parts.Count) { throw new ArgumentOutOfRangeException(nameof(request), "OpenEXR part index exceeds the file's parts."); }
            ExrWindow window = document.Parts[request.PageIndex].DataWindow;
            if (request.MaximumPixels is int maximum && (long)window.Width * window.Height > maximum)
            { throw new InvalidDataException("OpenEXR data window exceeds MaximumPixels."); }
            return ValueTask.FromResult(ExrScanlineDecoder.Decode(document, request.PageIndex, options.Layer, allocator, cancellationToken));
        }
    }
}
