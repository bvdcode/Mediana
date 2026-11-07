// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Bmp
{
    /// <summary>Decodes Windows/OS2 bitmaps to independently owned straight RGBA8 pixels.
    /// Supports palettes, RGB, bitfields and RLE4/RLE8. Output is budgeted; parsing borrows encoded data.</summary>
    public class BmpImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "bmp.managed.image";
        public int ProbeLength => 2;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("BM"u8);
        private static DibImage Read(ReadOnlyMemory<byte> encoded)
        {
            if (encoded.Length < 26 || !encoded.Span.StartsWith("BM"u8)) { throw new InvalidDataException("Invalid BMP header."); }
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span[2..]);
            if (length > encoded.Length) { throw new InvalidDataException("Truncated BMP file."); }
            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span[10..]) - 14);
            return new(encoded[14..], offset, false);
        }
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DibImage image = Read(encoded);
            return ValueTask.FromResult(Inspect(image, "bmp"));
        }
        internal static ImageInfo Inspect(DibImage image, string format, int? pages = null) => new(format,
            image.Width, image.Height, ImageDimensionUnit.Pixels, pageCount: pages,
            metadata: new("image", [new("media", "codec", "dib"), new("bmp", "bit_depth", image.Depth),
                new("bmp", "compression", image.Compression), new("bmp", "top_down", image.TopDown),
                new("bmp", "palette_entries", image.PaletteCount), new("bmp", "alpha_mask", image.Alpha)]));
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));
        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            return DecodeDib(Read(encoded), request, allocator, cancellationToken);
        }
        internal static VideoFrame DecodeDib(DibImage image, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken token)
        {
            if (request.MaximumPixels is int limit && (long)image.Width * image.Height > limit)
            { throw new InvalidDataException("DIB source exceeds MaximumPixels."); }
            if (image.Compression is 4 or 5)
            {
                IImageDecoder decoder = image.Compression switch
                {
                    4 => new JpegImageDecoder(new() { MaximumPixels = request.MaximumPixels ?? Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }),
                    5 => new PngImageDecoder(new() { MaximumPixels = request.MaximumPixels ?? Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }),
                    _ => throw new InvalidDataException("Invalid embedded DIB codec.")
                };
                VideoFrame embedded = ((ISynchronousImageDecoder)decoder).Decode(image.Data[image.PixelOffset..], request, allocator, token);
                if (embedded.Format.Width != image.Width || embedded.Format.Height != image.Height)
                { embedded.Dispose(); throw new InvalidDataException("Embedded DIB image dimensions disagree with its header."); }
                return embedded;
            }
            VideoFormat format = new(image.Width, image.Height, VideoPixelFormat.Rgba32, image.Color,
                SampleAspectRatio.Square, image.ColorDescription);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[0].EndOffset));
            DibPixelReader.Read(image, writable.Memory.Span, token);
            token.ThrowIfCancellationRequested();
            using MediaBuffer pixels = writable.Publish();
            return new(format, pixels, layout);
        }
    }
}
