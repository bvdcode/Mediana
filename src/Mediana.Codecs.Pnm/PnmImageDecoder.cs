// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Pnm
{
    /// <summary>Reads plain and binary PBM/PGM/PPM, including binary image sequences and 16-bit big-endian samples.
    /// PGM/PPM retain the format's BT.709 interpretation; samples are normalized to straight RGBA8.</summary>
    public class PnmImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "pnm.managed.image";
        public int ProbeLength => 3;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.Pages;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.Length >= 3 &&
            prefix[0] == 'P' && prefix[1] is >= (byte)'1' and <= (byte)'6' && PnmTokens.White(prefix[2]);

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<PnmImage> images = PnmImage.Read(encoded, cancellationToken);
            PnmImage first = images[0];
            List<MediaMetadata> pages = [];
            for (int i = 0; i < images.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PnmImage item = images[i];
                pages.Add(new("page/" + i, [new("pnm", "dimensions", item.Width, item.Height),
                    new("pnm", "type", "P" + item.Kind), new("pnm", "maximum_value", item.Maximum)]));
            }
            return ValueTask.FromResult(new ImageInfo(first.Format, first.Width, first.Height, ImageDimensionUnit.Pixels,
                images.Count, metadata: new("image", children: pages)));
        }

        public ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Decode(encoded, request, allocator, cancellationToken));

        public VideoFrame Decode(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            cancellationToken.ThrowIfCancellationRequested();
            List<PnmImage> images = PnmImage.Read(encoded, cancellationToken);
            if (request.PageIndex >= images.Count) { throw new ArgumentOutOfRangeException(nameof(request)); }
            PnmImage image = images[request.PageIndex];
            if (request.MaximumPixels is int limit && (long)image.Width * image.Height > limit)
            {
                throw new InvalidDataException("PNM source exceeds MaximumPixels.");
            }
            VideoColorInfo color = image.Bitmap ? VideoColorInfo.Srgb : VideoColorInfo.Srgb with { Transfer = TransferFunction.Bt709Oetf };
            VideoFormat format = new(image.Width, image.Height, VideoPixelFormat.Rgba32, color, SampleAspectRatio.Square);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)layout[0].EndOffset));
            ReadPixels(image, output.Span, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer pixels = output.Publish();
            return new(format, pixels, layout);
        }

        private static void ReadPixels(PnmImage image, Span<byte> output, CancellationToken token)
        {
            ReadOnlySpan<byte> data = image.Raster.Span;
            PnmTokens text = new(data, token);
            int bytesPerSample = image.Maximum < 256 ? 1 : 2, position = 0;
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < image.Width; x++)
                {
                    if ((x & 255) == 0) { token.ThrowIfCancellationRequested(); }
                    int destination = (y * image.Width + x) * 4;
                    for (int channel = 0; channel < image.Channels; channel++)
                    {
                        int value;
                        if (image.Plain) { value = image.Bitmap ? text.Bit() : text.Number(); }
                        else if (image.Bitmap) { value = (data[y * ((image.Width + 7) / 8) + x / 8] >> (7 - (x & 7))) & 1; }
                        else
                        {
                            value = bytesPerSample == 1 ? data[position] : BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
                            position += bytesPerSample;
                        }
                        if (value > image.Maximum) { throw new InvalidDataException("PNM sample exceeds the maximum value."); }
                        output[destination + channel] = image.Bitmap ? (byte)((1 - value) * 255)
                            : (byte)((value * 255 + image.Maximum / 2) / image.Maximum);
                    }
                    if (image.Channels == 1) { output[destination + 1] = output[destination]; output[destination + 2] = output[destination]; }
                    output[destination + 3] = 255;
                }
            }
        }
    }
}
