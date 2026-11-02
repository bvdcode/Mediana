// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal ref struct JpegExif(ReadOnlySpan<byte> data, CancellationToken token)
    {
        private readonly ReadOnlySpan<byte> bytes = data;
        private bool little;
        private uint first;
        private uint colorDirectory;
        private ulong? xResolution;
        private ulong? yResolution;
        internal OrthogonalTransform Orientation { get; private set; } = OrthogonalTransform.Identity;
        internal bool Centered { get; private set; } = true;
        internal ushort? ColorSpace { get; private set; }
        internal bool CustomColor { get; private set; }

        internal void Read()
        {
            if (bytes.Length < 8) { throw new InvalidDataException("Truncated JPEG Exif TIFF header."); }
            little = bytes[..2] switch
            {
                [73, 73] => true,
                [77, 77] => false,
                _ => throw new InvalidDataException("Invalid JPEG Exif byte order.")
            };
            if (Short(2) != 42) { throw new InvalidDataException("Invalid JPEG Exif TIFF identifier."); }
            first = Long(4);
            ReadDirectory(first, false);
            if (colorDirectory != 0)
            {
                if (colorDirectory == first) { throw new InvalidDataException("Cyclic JPEG Exif directory."); }
                ReadDirectory(colorDirectory, true);
            }
            ulong x = xResolution ?? ((72UL << 32) | 1);
            ulong y = yResolution ?? ((72UL << 32) | 1);
            if ((x >> 32) * (uint)y != (y >> 32) * (uint)x)
            {
                throw new InvalidDataException("JPEG Exif requires equal horizontal and vertical resolution.");
            }
            token.ThrowIfCancellationRequested();
        }

        private void ReadDirectory(uint offset, bool color)
        {
            if (offset < 8 || (offset & 1) != 0) { throw new InvalidDataException("Invalid JPEG Exif directory offset."); }
            int start = Position(offset, 2);
            int count = Short(start);
            Position(offset, checked(2 + count * 12 + 4));
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                int entry = start + 2 + i * 12;
                int tag = Short(entry);
                if (tag <= previous) { throw new InvalidDataException("JPEG Exif tags must be ordered and unique."); }
                previous = tag;
                if (color)
                {
                    switch (tag)
                    {
                        case 40961:
                            ColorSpace = Scalar(entry, 3);
                            if (ColorSpace is not (1 or 65535)) { throw new InvalidDataException("Reserved JPEG Exif color space."); }
                            break;
                        case 42240:
                            Rational(entry);
                            CustomColor = true;
                            break;
                    }
                }
                else
                {
                    switch (tag)
                    {
                        case 274: Orientation = ReadOrientation(Scalar(entry, 3)); break;
                        case 282: xResolution = Rational(entry); break;
                        case 283: yResolution = Rational(entry); break;
                        case 531:
                            Centered = Scalar(entry, 3) switch
                            {
                                1 => true,
                                2 => false,
                                _ => throw new InvalidDataException("Reserved JPEG Exif chroma position.")
                            };
                            break;
                        case 34665:
                            if (Short(entry + 2) != 4 || Long(entry + 4) != 1) { throw new InvalidDataException("Invalid JPEG Exif directory pointer."); }
                            colorDirectory = Long(entry + 8);
                            if (colorDirectory == 0) { throw new InvalidDataException("Empty JPEG Exif color directory pointer."); }
                            break;
                        case 301:
                        case 318:
                        case 319:
                        case 529:
                        case 532:
                            CustomColor = true;
                            break;
                    }
                }
            }
        }

        private ushort Scalar(int entry, int type)
        {
            if (Short(entry + 2) != type || Long(entry + 4) != 1) { throw new InvalidDataException("Invalid JPEG Exif scalar type or count."); }
            return Short(entry + 8);
        }

        private ulong Rational(int entry)
        {
            if (Short(entry + 2) != 5 || Long(entry + 4) != 1) { throw new InvalidDataException("Invalid JPEG Exif rational type or count."); }
            uint offset = Long(entry + 8);
            if (offset < 8 || (offset & 1) != 0) { throw new InvalidDataException("Invalid JPEG Exif rational offset."); }
            int position = Position(offset, 8);
            uint numerator = Long(position), denominator = Long(position + 4);
            if (numerator == 0 || denominator == 0) { throw new InvalidDataException("Invalid JPEG Exif rational value."); }
            return ((ulong)numerator << 32) | denominator;
        }

        private int Position(uint offset, int length)
        {
            if (offset > bytes.Length || length > bytes.Length - offset) { throw new InvalidDataException("Truncated JPEG Exif directory or value."); }
            return (int)offset;
        }

        private ushort Short(int offset) => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]);
        private uint Long(int offset) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);

        private static OrthogonalTransform ReadOrientation(int value) => OrthogonalTransform.FromMatrix(value switch
        {
            1 => VideoDisplayMatrix.Identity,
            2 => new(-1, 0, 0, 0, 1, 0, 0, 0, 1),
            3 => new(-1, 0, 0, 0, -1, 0, 0, 0, 1),
            4 => new(1, 0, 0, 0, -1, 0, 0, 0, 1),
            5 => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
            6 => new(0, 1, 0, -1, 0, 0, 0, 0, 1),
            7 => new(0, -1, 0, -1, 0, 0, 0, 0, 1),
            8 => new(0, -1, 0, 1, 0, 0, 0, 0, 1),
            _ => throw new InvalidDataException("Reserved JPEG Exif orientation.")
        });
    }
}
