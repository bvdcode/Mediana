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
            JpegImageDecoder decoder = new(new() { MaximumPixels = Array.MaxLength / 4, MaximumEncodedBytes = Array.MaxLength }, sourceColorSpace: samples.Photometric switch
            {
                2 => JpegColorSpace.Rgb,
                6 => JpegColorSpace.YCbCr,
                0 or 1 => null,
                _ => throw new NotSupportedException("Unsupported TIFF JPEG photometric interpretation.")
            });
            using VideoFrame frame = decoder.Decode(encoded, new(), allocator, token);
            if (frame.Format.Width != width || frame.Format.Height != height)
            { throw new InvalidDataException("TIFF JPEG block dimensions disagree with its tile or strip."); }
            if (samples.Planar != 1 || samples.Channels is not (1 or 3)) { throw new NotSupportedException("Unsupported TIFF JPEG plane or alpha layout."); }
            int outputWidth = samples.Orientation >= 5 ? samples.Height : samples.Width;
            for (int y = 0; y < Math.Min(height, samples.Height - top); y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < Math.Min(width, samples.Width - left); x++)
                {
                    (int targetX, int targetY) = samples.Position(left + x, top + y);
                    frame.GetRow(0, y).Slice(x * 4, 4).CopyTo(pixels.Slice((targetY * outputWidth + targetX) * 4, 4));
                }
            }
        }
    }
}
