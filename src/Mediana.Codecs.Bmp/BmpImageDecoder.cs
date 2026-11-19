// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
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
        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Images is null)
            {
                return await BitmapImageDecoders.Default.DecodeAsync(encoded, request, allocator, cancellationToken).ConfigureAwait(false);
            }
            return await DecodeDibAsync(Read(encoded), request, allocator, cancellationToken).ConfigureAwait(false);
        }

        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Images is null)
            {
                return BitmapImageDecoders.Default.Decode(encoded, request, allocator, cancellationToken);
            }
            return DecodeDib(Read(encoded), request, allocator, cancellationToken);
        }

        internal static async ValueTask<VideoFrame> DecodeDibAsync(DibImage image, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken token)
        {
            ValidateSize(image, request);
            if (image.Compression is 4 or 5)
            {
                VideoFrame embedded = await request.Images!.DecodeAsync(image.Data[image.PixelOffset..], request, allocator, token).ConfigureAwait(false);
                return ValidateDimensions(embedded, image.Width, image.Height);
            }
            return DecodePixels(image, allocator, token);
        }

        internal static VideoFrame DecodeDib(DibImage image, ImageDecodeRequest request, MediaBufferAllocator allocator, CancellationToken token)
        {
            ValidateSize(image, request);
            if (image.Compression is 4 or 5)
            {
                VideoFrame embedded = request.Images!.Decode(image.Data[image.PixelOffset..], request, allocator, token);
                return ValidateDimensions(embedded, image.Width, image.Height);
            }
            return DecodePixels(image, allocator, token);
        }

        private static void ValidateSize(DibImage image, ImageDecodeRequest request)
        {
            ReadOnlySpan<byte> payload = image.Data.Span[image.PixelOffset..];
            if (image.Compression == 4 && !payload.StartsWith<byte>([0xFF, 0xD8]) ||
                image.Compression == 5 && !payload.StartsWith<byte>([137, 80, 78, 71, 13, 10, 26, 10]))
            {
                throw new InvalidDataException("Embedded DIB payload does not match its declared compression.");
            }
            if (request.MaximumPixels is int limit && (long)image.Width * image.Height > limit)
            {
                throw new InvalidDataException("DIB source exceeds MaximumPixels.");
            }
        }

        internal static VideoFrame ValidateDimensions(VideoFrame frame, int width, int height)
        {
            if (frame.Format.Width != width || frame.Format.Height != height)
            {
                frame.Dispose();
                throw new InvalidDataException("Embedded image dimensions disagree with its directory or header.");
            }
            return frame;
        }

        private static VideoFrame DecodePixels(DibImage image, MediaBufferAllocator allocator, CancellationToken token)
        {
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
