// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Formats;
using Mediana.Formats.Metadata;

namespace Mediana.Codecs.Png
{
    internal static class PngImageParser
    {
        public static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

        public static PngImageData Read(ReadOnlyMemory<byte> encoded, PngDecodeOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (encoded.Length > options.MaximumEncodedBytes)
            {
                throw new NotSupportedException("PNG input exceeds the encoded byte limit.");
            }
            if (!encoded.Span.StartsWith(Signature))
            {
                throw new InvalidDataException("Invalid PNG signature.");
            }
            PngImageData? image = null;
            PngColorChunks colors = new();
            HashSet<uint> singletons = [];
            int offset = Signature.Length;
            int chunks = 0;
            bool dataEnded = false;
            while (offset < encoded.Length)
            {
                token.ThrowIfCancellationRequested();
                if (++chunks > options.MaximumChunks)
                {
                    throw new NotSupportedException("PNG input exceeds the chunk count limit.");
                }
                ReadOnlySpan<byte> remaining = encoded.Span[offset..];
                if (remaining.Length < 12)
                {
                    throw new InvalidDataException("Truncated PNG chunk.");
                }
                uint length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
                if (length > int.MaxValue || length > remaining.Length - 12)
                {
                    throw new InvalidDataException("Invalid PNG chunk length.");
                }
                int size = (int)length;
                ReadOnlySpan<byte> type = remaining.Slice(4, 4);
                foreach (byte letter in type)
                {
                    if (letter is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'))
                    {
                        throw new InvalidDataException("Invalid PNG chunk type.");
                    }
                }
                if (IeeeCrc32.Compute(remaining.Slice(4, size + 4), token) != BinaryPrimitives.ReadUInt32BigEndian(remaining[(size + 8)..]))
                {
                    throw new InvalidDataException("PNG chunk CRC mismatch.");
                }
                uint code = BinaryPrimitives.ReadUInt32BigEndian(type);
                if (code != 0x49444154 && IsSingleton(code) && !singletons.Add(code))
                {
                    throw new InvalidDataException("Duplicate PNG singleton chunk.");
                }
                ReadOnlyMemory<byte> payload = encoded.Slice(offset + 8, size);
                int end = offset + size + 12;
                if (image is null)
                {
                    if (code != 0x49484452)
                    {
                        throw new InvalidDataException("PNG must start with IHDR.");
                    }
                    image = ReadHeader(payload.Span, options);
                }
                else
                {
                    bool beforeData = image.DataStart < 0;
                    if (!beforeData && code != 0x49444154)
                    {
                        dataEnded = true;
                    }
                    switch (code)
                    {
                        case 0x49484452:
                            throw new InvalidDataException("Duplicate PNG header.");
                        case 0x504c5445:
                            if (!beforeData || !image.Transparency.IsEmpty || image.ColorType is 0 or 4 || size is 0 or > 768 || size % 3 != 0 ||
                                (image.ColorType == 3 && size / 3 > 1 << image.Depth))
                            {
                                throw new InvalidDataException("Invalid PNG palette or palette order.");
                            }
                            image.Palette = payload;
                            break;
                        case 0x74524e53:
                            ValidateTransparency(image, payload.Span, beforeData);
                            image.Transparency = payload;
                            break;
                        case 0x49444154:
                            if (dataEnded || (image.ColorType == 3 && image.Palette.IsEmpty))
                            {
                                throw new InvalidDataException("Invalid PNG image data order.");
                            }
                            if (beforeData)
                            {
                                image.DataStart = offset;
                            }
                            image.DataEnd = end;
                            break;
                        case 0x49454e44:
                            if (size != 0 || beforeData || end != encoded.Length)
                            {
                                throw new InvalidDataException("Invalid PNG end or trailing file data.");
                            }
                            colors.Apply(image);
                            if (image.HasExif) { image.Orientation = ExifOrientation.Read(image.Exif, token); }
                            return image;
                        default:
                            if ((type[0] & 32) == 0)
                            {
                                throw new NotSupportedException("Unknown critical PNG chunk.");
                            }
                            colors.Read(code, payload.Span, beforeData, image);
                            if (code == 0x65584966) { image.Exif = payload; }
                            break;
                    }
                }
                offset = end;
            }
            throw new InvalidDataException("PNG image is missing IEND.");
        }

        private static bool IsSingleton(uint type) => type is
            0x49484452 or 0x504c5445 or 0x49454e44 or 0x74524e53 or 0x73524742 or 0x63494350 or
            0x67414d41 or 0x6348524d or 0x69434350 or 0x70485973 or 0x6163544c or 0x65584966;

        private static PngImageData ReadHeader(ReadOnlySpan<byte> header, PngDecodeOptions options)
        {
            if (header.Length != 13)
            {
                throw new InvalidDataException("Invalid PNG IHDR length.");
            }
            uint width = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            {
                throw new InvalidDataException("Invalid PNG dimensions.");
            }
            if ((long)width * height > options.MaximumPixels)
            {
                throw new NotSupportedException("PNG dimensions exceed the pixel count limit.");
            }
            int depth = header[8];
            int colorType = header[9];
            bool valid = colorType switch
            {
                0 => depth is 1 or 2 or 4 or 8 or 16,
                2 or 4 or 6 => depth is 8 or 16,
                3 => depth is 1 or 2 or 4 or 8,
                _ => false
            };
            if (!valid)
            {
                throw new NotSupportedException("Unsupported PNG bit depth or color type.");
            }
            if (header[10] != 0 || header[11] != 0 || header[12] > 1)
            {
                throw new NotSupportedException("Unsupported PNG compression, filtering or interlace method.");
            }
            return new((int)width, (int)height, depth, colorType, header[12] == 1);
        }

        private static void ValidateTransparency(PngImageData image, ReadOnlySpan<byte> data, bool beforeData)
        {
            bool valid = image.ColorType switch
            {
                0 => data.Length == 2,
                2 => data.Length == 6,
                3 => data.Length > 0 && data.Length <= image.Palette.Length / 3,
                4 or 6 => false,
                _ => throw new InvalidDataException("Invalid PNG color type.")
            };
            if (!beforeData || !valid)
            {
                throw new InvalidDataException("Invalid PNG transparency or chunk order.");
            }
        }
    }
}
