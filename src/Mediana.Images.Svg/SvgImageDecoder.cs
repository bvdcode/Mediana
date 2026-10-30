// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.Images.Svg
{
    /// <summary>Registered SVG rasterization with bounded XML and rendering. No external resources are loaded implicitly.</summary>
    public class SvgImageDecoder(SvgReaderOptions? readerOptions = null, SvgRenderOptions? renderOptions = null) : IImageDecoder
    {
        public string ImplementationId => "svg.managed.image";
        public int ProbeLength => 512;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;

        /// <summary>Recognizes XML byte signatures. Complete parsing validates the SVG root and encoding.</summary>
        public bool CanDecode(ReadOnlySpan<byte> prefix)
        {
            if (prefix.StartsWith<byte>([0xef, 0xbb, 0xbf]))
            {
                prefix = prefix[3..];
            }
            return prefix.TrimStart(" \r\n\t"u8).StartsWith("<"u8)
                || prefix.StartsWith<byte>([0xfe, 0xff]) || prefix.StartsWith<byte>([0xff, 0xfe])
                || prefix.StartsWith<byte>([0, 0, 0xfe, 0xff]) || prefix.StartsWith<byte>([0, 0, 0, 0x3c])
                || prefix.StartsWith<byte>([0, 0x3c, 0, 0x3f]);
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            SvgDocument document = SvgReader.Read(encoded, readerOptions, cancellationToken);
            PixelBuffer pixels = SvgRenderer.Render(document, renderOptions, cancellationToken);
            return ValueTask.FromResult(pixels.ToVideoFrame(allocator, cancellationToken));
        }
    }
}
