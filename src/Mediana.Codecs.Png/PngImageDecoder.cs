// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Png
{
    /// <summary>Decodes static PNG to straight RGBA32. All standard color types, sample depths, filters and Adam7 passes
    /// are supported. Sample precision is scaled to 8 bits with nearest rounding; no color transform is applied.
    /// Borrows encoded data only during the call. The allocator accounts for the output and two scanlines;
    /// BCL zlib workspace and bounded chunk metadata are separate. ICC/gamma conversion, Exif and animation are unsupported.</summary>
    public class PngImageDecoder : IImageDecoder, IImageInfoReader
    {
        private readonly PngDecodeOptions options;

        public PngImageDecoder(PngDecodeOptions? options = null)
        {
            this.options = options ?? new();
            this.options.Validate();
        }

        public string ImplementationId => "png.managed.image";
        public int ProbeLength => 8;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix) => prefix.StartsWith(PngImageParser.Signature);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            PngImageData image = Read(encoded, cancellationToken);
            return ValueTask.FromResult(new ImageInfo("png", image.Width, image.Height, ImageDimensionUnit.Pixels));
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            PngImageData image = Read(encoded, cancellationToken);
            VideoFormat format = new(image.Width, image.Height, VideoPixelFormat.Rgba32, image.Color, image.Aspect, image.ColorDescription);
            long rowBytes = ((long)image.Width * image.Channels * image.Depth + 7) / 8;
            if (rowBytes > int.MaxValue / 2)
            {
                throw new NotSupportedException("PNG scanline storage exceeds the supported byte count.");
            }
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer pixels = allocator.Allocate(checked((int)layout[0].EndOffset));
            int rowCapacity = (int)rowBytes;
            using WritableMediaBuffer rows = allocator.Allocate(checked(rowCapacity * 2));
            using PngIdatReadStream compressed = new(encoded[image.DataStart..image.DataEnd], cancellationToken);
            using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
            if (image.Interlaced)
            {
                ReadOnlySpan<int> startX = [0, 4, 0, 2, 0, 1, 0];
                ReadOnlySpan<int> startY = [0, 0, 4, 0, 2, 0, 1];
                ReadOnlySpan<int> stepX = [8, 8, 4, 4, 2, 2, 1];
                ReadOnlySpan<int> stepY = [8, 8, 8, 4, 4, 2, 2];
                for (int pass = 0; pass < 7; pass++)
                {
                    DecodePass(image, zlib, pixels.Memory.Span, rows.Memory.Span, rowCapacity,
                        startX[pass], startY[pass], stepX[pass], stepY[pass], cancellationToken);
                }
            }
            else
            {
                DecodePass(image, zlib, pixels.Memory.Span, rows.Memory.Span, rowCapacity, 0, 0, 1, 1, cancellationToken);
            }
            if (zlib.ReadByte() != -1)
            {
                throw new InvalidDataException("PNG contains more decompressed data than its declared scanlines.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = pixels.Publish();
            return ValueTask.FromResult(new VideoFrame(format, buffer, layout));
        }

        private PngImageData Read(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            PngImageData image = PngImageParser.Read(encoded, options, token);
            if (image.Animated || image.HasExif)
            {
                throw new NotSupportedException("PNG animation and Exif presentation transforms are not supported.");
            }
            return image;
        }

        private static void DecodePass(PngImageData image, ZLibStream zlib, Span<byte> pixels, Span<byte> rows, int capacity,
            int startX, int startY, int stepX, int stepY, CancellationToken token)
        {
            if (startX >= image.Width || startY >= image.Height)
            {
                return;
            }
            int width = (image.Width - startX + stepX - 1) / stepX;
            int rowBytes = checked((int)(((long)width * image.Channels * image.Depth + 7) / 8));
            int stride = Math.Max(1, (image.Channels * image.Depth + 7) / 8);
            Span<byte> previous = rows.Slice(0, rowBytes);
            Span<byte> current = rows.Slice(capacity, rowBytes);
            previous.Clear();
            for (int y = startY; y < image.Height; y += stepY)
            {
                token.ThrowIfCancellationRequested();
                int filter = zlib.ReadByte();
                if (filter < 0)
                {
                    throw new InvalidDataException("PNG is missing a scanline filter.");
                }
                ReadRow(zlib, current, token);
                PngScanline.Unfilter(filter, current, previous, stride, token);
                PngScanline.Expand(image, current, pixels[((y * image.Width + startX) * 4)..], width, stepX, token);
                Span<byte> temporary = previous;
                previous = current;
                current = temporary;
            }
        }

        private static void ReadRow(ZLibStream zlib, Span<byte> row, CancellationToken token)
        {
            while (!row.IsEmpty)
            {
                token.ThrowIfCancellationRequested();
                int read = zlib.Read(row[..Math.Min(row.Length, 16 * 1024)]);
                if (read == 0)
                {
                    throw new InvalidDataException("PNG is missing scanline bytes.");
                }
                row = row[read..];
            }
        }
    }
}
