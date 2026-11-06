// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Codecs.Compression;
using System.Runtime.InteropServices;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats.Metadata;
using Mediana.Formats.Tiff;

namespace Mediana.Codecs.Tiff
{
    /// <summary>Reads classic TIFF and BigTIFF directories and decodes selected pages to straight RGBA8.
    /// Supports strips/tiles, chunky/separate samples, palettes, grayscale/RGB/CMYK and declared alpha.
    /// Encoded input is borrowed; output and decoded block workspace share the allocator.</summary>
    public partial class TiffImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "tiff.managed.image";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("II*\0"u8)
            || prefix.StartsWith("MM\0*"u8) || prefix.StartsWith("II+\0"u8) || prefix.StartsWith("MM\0+"u8);
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            TiffImageData document = new(encoded, cancellationToken);
            TiffDirectory first = document.Pages[0];
            int width = checked((int)first.Number(256)), height = checked((int)first.Number(257));
            int orientation = checked((int)first.Number(274, 1));
            MediaMetadata metadata = ExifMetadataReader.Read(document.Reader, cancellationToken);
            return ValueTask.FromResult(new ImageInfo("tiff", orientation >= 5 ? height : width, orientation >= 5 ? width : height,
                ImageDimensionUnit.Pixels, document.Pages.Count, metadata: new("image",
                    [new("tiff", "big_tiff", document.Reader.BigTiff), new("tiff", "little_endian", document.Reader.LittleEndian)], [metadata])));
        }
        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) => ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));
        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features); cancellationToken.ThrowIfCancellationRequested();
            TiffImageData document = new(encoded, cancellationToken);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.PageIndex, document.Pages.Count);
            TiffDirectory page = document.Pages[request.PageIndex];
            TiffSamples samples = new(page, document.Reader.LittleEndian);
            if (request.MaximumPixels is int limit && (long)samples.Width * samples.Height > limit)
            { throw new InvalidDataException("TIFF source exceeds MaximumPixels."); }
            int compression = checked((int)page.Number(259, 1));
            if (samples.Photometric == 6 && compression != 7)
            { throw new NotSupportedException("Subsampled TIFF YCbCr requires JPEG block decoding."); }
            bool swap = samples.Orientation >= 5;
            VideoColorDescription? color = page.Fields.TryGetValue(34675, out TiffField? profile)
                ? new IccColorDescription(profile.Bytes.Span, false) : null;
            VideoFormat format = new(swap ? samples.Height : samples.Width, swap ? samples.Width : samples.Height,
                VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square, color);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)layout[0].EndOffset));
            for (int row = 0; row < format.Height; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<byte> pixels = output.Memory.Span.Slice(row * format.Width * 4, format.Width * 4);
                pixels.Clear();
                for (int offset = 3; offset < pixels.Length; offset += 4) { pixels[offset] = 255; }
            }
            ReadBlocks(document, page, samples, compression, output.Memory.Span, allocator, cancellationToken);
            if (samples.Alpha == 1)
            {
                for (int y = 0; y < format.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<byte> row = output.Memory.Span.Slice(y * format.Width * 4, format.Width * 4);
                    for (int x = 0; x < format.Width; x++)
                    {
                        int offset = x * 4, alpha = row[offset + 3];
                        for (int channel = 0; channel < 3; channel++)
                        { row[offset + channel] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (row[offset + channel] * 255 + alpha / 2) / alpha); }
                    }
                }
            }
            using MediaBuffer published = output.Publish();
            return new(format, published, layout);
        }
        private static void ReadBlocks(TiffImageData document, TiffDirectory page, TiffSamples samples, int compression,
            Span<byte> pixels, MediaBufferAllocator allocator, CancellationToken token)
        {
            bool tiled = page.Fields.ContainsKey(324);
            int tileWidth = tiled ? checked((int)page.Number(322)) : samples.Width;
            int tileHeight = tiled ? checked((int)page.Number(323)) : checked((int)Math.Min((ulong)samples.Height, page.Number(278, (ulong)samples.Height)));
            if (tileWidth <= 0 || tileHeight <= 0) { throw new InvalidDataException("Invalid TIFF block dimensions."); }
            int across = checked((int)(((long)samples.Width + tileWidth - 1) / tileWidth));
            int down = checked((int)(((long)samples.Height + tileHeight - 1) / tileHeight));
            int planes = samples.Planar == 2 ? samples.Channels : 1, channels = samples.Planar == 2 ? 1 : samples.Channels;
            ulong[] offsets = page.Numbers(tiled ? (ushort)324 : (ushort)273), lengths = page.Numbers(tiled ? (ushort)325 : (ushort)279);
            if (offsets.Length != checked(across * down * planes) || lengths.Length != offsets.Length)
            { throw new InvalidDataException("TIFF block offsets and lengths disagree with its dimensions."); }
            for (int plane = 0; plane < planes; plane++)
            {
                for (int index = 0; index < across * down; index++)
                {
                    token.ThrowIfCancellationRequested();
                    int x = index % across * tileWidth, y = index / across * tileHeight;
                    int rows = tiled ? tileHeight : Math.Min(tileHeight, samples.Height - y);
                    int blockIndex = plane * across * down + index;
                    ReadOnlyMemory<byte> encoded = document.Reader.Slice(offsets[blockIndex], lengths[blockIndex]);
                    if (compression == 7) { ReadJpeg(page, samples, encoded, pixels, x, y, tileWidth, rows, allocator, token); continue; }
                    int rowBytes = samples.RowBytes(tileWidth, channels);
                    using WritableMediaBuffer decoded = allocator.Allocate(checked(rowBytes * rows));
                    if (compression is 2 or 3 or 4)
                    {
                        if (samples.Depth != 1 || channels != 1 || samples.Photometric is not (0 or 1))
                        { throw new InvalidDataException("CCITT TIFF requires bilevel grayscale samples."); }
                        ulong fillOrder = page.Number(266, 1);
                        if (fillOrder is not (1 or 2)) { throw new InvalidDataException("Invalid TIFF FillOrder."); }
                        using WritableMediaBuffer workspace = allocator.Allocate(checked((tileWidth + 2) * 2 * sizeof(int)));
                        FaxDecoder.Decode(encoded.Span, decoded.Memory.Span, tileWidth, rows, compression,
                            checked((uint)page.Number(compression == 3 ? (ushort)292 : (ushort)293)), fillOrder == 2,
                            MemoryMarshal.Cast<byte, int>(workspace.Memory.Span), token);
                    }
                    else { TiffCompression.Decode(encoded, decoded.Memory.Span, compression, token); }
                    if (compression is not (2 or 3 or 4) && page.Number(266, 1) == 2)
                    {
                        for (int i = 0; i < decoded.Memory.Length; i++) { decoded.Memory.Span[i] = Reverse(decoded.Memory.Span[i]); }
                    }
                    else if (compression is not (2 or 3 or 4) && page.Number(266, 1) != 1) { throw new InvalidDataException("Invalid TIFF FillOrder."); }
                    samples.Predict(decoded.Memory.Span, rowBytes, channels);
                    for (int row = 0; row < Math.Min(rows, samples.Height - y); row++)
                    {
                        token.ThrowIfCancellationRequested();
                        samples.Paint(decoded.Memory.Span.Slice(row * rowBytes, rowBytes), pixels, y + row, x, Math.Min(tileWidth, samples.Width - x), plane);
                    }
                }
            }
        }
        private static byte Reverse(byte value)
        {
            int result = ((value & 0x55) << 1) | ((value >> 1) & 0x55);
            result = ((result & 0x33) << 2) | ((result >> 2) & 0x33);
            return (byte)((result << 4) | (result >> 4));
        }
    }
}
