// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Tiff;

namespace Mediana.Codecs.Tiff
{
    public partial class TiffImageDecoder
    {
        private static void ReadJpeg(TiffDirectory page, TiffSamples samples, ReadOnlyMemory<byte> encoded, Span<byte> pixels,
            int left, int top, int width, int height, MediaBufferAllocator allocator, CancellationToken token)
        {
            if (samples.Depth != 8 || samples.Planar != 1 || samples.Alpha != 0 || samples.Channels is not (1 or 3 or 4))
            {
                throw new NotSupportedException("TIFF JPEG requires chunky eight-bit grayscale, RGB, YCbCr or CMYK samples.");
            }
            JpegColorSpace color = samples.Photometric switch
            {
                0 or 1 or 2 => JpegColorSpace.Rgb,
                5 => JpegColorSpace.Cmyk,
                6 => JpegColorSpace.YCbCr,
                _ => throw new NotSupportedException("Unsupported TIFF JPEG photometric interpretation.")
            };
            page.Fields.TryGetValue(347, out TiffField? tables);
            if (tables is not null && (tables.Bytes.Length < 4 || !tables.Bytes.Span.StartsWith(new byte[] { 255, 216 })
                || !tables.Bytes.Span.EndsWith(new byte[] { 255, 217 }) || encoded.Length < 2 || !encoded.Span.StartsWith(new byte[] { 255, 216 })))
            { throw new InvalidDataException("Invalid TIFF JPEG table boundaries."); }
            using WritableMediaBuffer? combined = tables is not null
                ? allocator.Allocate(checked(tables.Bytes.Length + encoded.Length - 4)) : null;
            if (combined is not null)
            {
                tables!.Bytes[..^2].CopyTo(combined.Memory); encoded[2..].CopyTo(combined.Memory[(tables.Bytes.Length - 2)..]);
                encoded = combined.Memory;
            }
            using JpegImage image = JpegDecoder.DecodeComponents(encoded, allocator,
                new() { MaximumPixels = Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }, token);
            if (image.Width != width || image.Height != height || image.ComponentCount != samples.Channels)
            { throw new InvalidDataException("TIFF JPEG block dimensions disagree with its tile or strip."); }
            if (samples.Orientation == 1 && samples.Photometric != 0 && left == 0 &&
                width == samples.Width && height <= samples.Height - top)
            {
                image.WriteRgbaTo(pixels.Slice(checked(top * width * 4), checked(height * width * 4)), color, token);
                return;
            }
            using WritableMediaBuffer decoded = allocator.Allocate(checked(width * height * 4));
            image.WriteRgbaTo(decoded.Span, color, token);
            int outputWidth = samples.Orientation >= 5 ? samples.Height : samples.Width;
            for (int y = 0; y < Math.Min(height, samples.Height - top); y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < Math.Min(width, samples.Width - left); x++)
                {
                    (int targetX, int targetY) = samples.Position(left + x, top + y);
                    Span<byte> source = decoded.Span.Slice((y * width + x) * 4, 4);
                    Span<byte> destination = pixels.Slice((targetY * outputWidth + targetX) * 4, 4);
                    source.CopyTo(destination);
                    if (samples.Photometric == 0)
                    {
                        destination[0] = (byte)(255 - source[0]);
                        destination[1] = (byte)(255 - source[1]);
                        destination[2] = (byte)(255 - source[2]);
                    }
                }
            }
        }
    }
}
