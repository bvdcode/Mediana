// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Qoi
{
    /// <summary>Decodes QOI 1.0 RGB/RGBA images to straight RGBA8. The output is budgeted; workspace is a 64-pixel index.</summary>
    public class QoiImageDecoder : ISynchronousImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "qoi.managed.image";
        public int ProbeLength => 4;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;
        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => prefix.StartsWith("qoif"u8);

        private static VideoFormat ReadHeader(ReadOnlySpan<byte> encoded)
        {
            if (encoded.Length < 22 || !encoded.StartsWith("qoif"u8) || encoded[12] is not (3 or 4) || encoded[13] > 1 ||
                !encoded[^8..].SequenceEqual<byte>([0, 0, 0, 0, 0, 0, 0, 1]))
            {
                throw new InvalidDataException("Invalid QOI header or end marker.");
            }
            uint width = BinaryPrimitives.ReadUInt32BigEndian(encoded[4..]);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(encoded[8..]);
            if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            {
                throw new InvalidDataException("Invalid QOI dimensions.");
            }
            VideoColorInfo color = encoded[13] switch
            {
                0 => VideoColorInfo.Srgb,
                1 => VideoColorInfo.Srgb with { Transfer = TransferFunction.Linear },
                _ => throw new InvalidDataException("Invalid QOI color space.")
            };
            return new((int)width, (int)height, VideoPixelFormat.Rgba32, color, SampleAspectRatio.Square);
        }

        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VideoFormat format = ReadHeader(encoded.Span);
            return ValueTask.FromResult(new ImageInfo("qoi", format.Width, format.Height, ImageDimensionUnit.Pixels,
                metadata: new("image", [new("qoi", "channels", (int)encoded.Span[12]), new("qoi", "color_space", (int)encoded.Span[13])])));
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
            VideoFormat format = ReadHeader(encoded.Span);
            if (request.MaximumPixels is int limit && (long)format.Width * format.Height > limit)
            {
                throw new InvalidDataException("QOI source exceeds MaximumPixels.");
            }
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)layout[0].EndOffset));
            DecodePixels(encoded.Span[14..^8], output.Span, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer pixels = output.Publish();
            return new(format, pixels, layout);
        }

        private static void DecodePixels(ReadOnlySpan<byte> data, Span<byte> output, CancellationToken token)
        {
            Span<uint> index = stackalloc uint[64];
            index.Clear();
            Span<uint> destination = MemoryMarshal.Cast<byte, uint>(output);
            uint pixel = 0xff000000;
            int offset = 0, written = 0, previousIndex = -1;
            while (written < destination.Length)
            {
                if ((written & 255) == 0) { token.ThrowIfCancellationRequested(); }
                byte code = ReadByte(data, ref offset);
                int run = 1;
                byte r = (byte)pixel, g = (byte)(pixel >> 8), b = (byte)(pixel >> 16), a = (byte)(pixel >> 24);
                if (code == 0xfe || code == 0xff)
                {
                    r = ReadByte(data, ref offset); g = ReadByte(data, ref offset); b = ReadByte(data, ref offset);
                    if (code == 0xff) { a = ReadByte(data, ref offset); }
                    pixel = Pack(r, g, b, a);
                }
                else
                {
                    switch (code >> 6)
                    {
                        case 0:
                            if (previousIndex == code) { throw new InvalidDataException("Repeated QOI index must use a run."); }
                            pixel = index[code];
                            break;
                        case 1:
                            pixel = Pack(unchecked((byte)(r + ((code >> 4) & 3) - 2)),
                                unchecked((byte)(g + ((code >> 2) & 3) - 2)), unchecked((byte)(b + (code & 3) - 2)), a);
                            break;
                        case 2:
                            int dg = (code & 63) - 32;
                            int difference = ReadByte(data, ref offset);
                            pixel = Pack(unchecked((byte)(r + dg + (difference >> 4) - 8)),
                                unchecked((byte)(g + dg)), unchecked((byte)(b + dg + (difference & 15) - 8)), a);
                            break;
                        case 3:
                            run = (code & 63) + 1;
                            break;
                    }
                }
                previousIndex = code < 64 ? code : -1;
                if (run > destination.Length - written) { throw new InvalidDataException("QOI run exceeds the image dimensions."); }
                index[(int)(((pixel & 255) * 3 + ((pixel >> 8) & 255) * 5 + ((pixel >> 16) & 255) * 7 + (pixel >> 24) * 11) & 63)] = pixel;
                destination.Slice(written, run).Fill(BitConverter.IsLittleEndian ? pixel : BinaryPrimitives.ReverseEndianness(pixel));
                written += run;
                token.ThrowIfCancellationRequested();
            }
            if (offset != data.Length) { throw new InvalidDataException("Unexpected QOI data after the final pixel."); }
        }

        private static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | g << 8 | b << 16 | a << 24);
        private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
        {
            if ((uint)offset >= data.Length) { throw new InvalidDataException("Truncated QOI pixel data."); }
            return data[offset++];
        }
    }
}
