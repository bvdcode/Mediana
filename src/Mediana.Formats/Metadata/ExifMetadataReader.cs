// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Metadata;

namespace Mediana.Formats.Metadata
{
    /// <summary>Inspects classic TIFF/Exif directories without decoding image samples or interpreting vendor MakerNotes.
    /// Native fractions, text and binary values remain distinct. Calendar text is not assigned a time zone.</summary>
    public class ExifMetadataReader
    {
        private readonly ReadOnlyMemory<byte> data;
        private readonly bool littleEndian;
        private readonly CancellationToken token;

        private ExifMetadataReader(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            this.data = data;
            this.token = token;
            littleEndian = data.Span switch
            {
                [73, 73, ..] => true,
                [77, 77, ..] => false,
                _ => throw new InvalidDataException("Invalid Exif TIFF byte order.")
            };
            if (Short(2) != 42)
            {
                throw new InvalidDataException("Invalid Exif TIFF identifier.");
            }
        }

        public static MediaMetadata Read(ReadOnlyMemory<byte> tiff, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ExifMetadataReader(tiff, cancellationToken).ReadDirectories();
        }

        private MediaMetadata ReadDirectories()
        {
            Stack<(uint Offset, string Scope, bool Exit)> pending = new();
            pending.Push((Long(4), "IFD0", false));
            HashSet<uint> active = [];
            HashSet<uint> completed = [];
            List<MediaMetadata> directories = [];
            while (pending.TryPop(out (uint Offset, string Scope, bool Exit) directory))
            {
                token.ThrowIfCancellationRequested();
                if (directory.Offset == 0) { continue; }
                if (directory.Exit)
                {
                    active.Remove(directory.Offset);
                    completed.Add(directory.Offset);
                    continue;
                }
                if (active.Contains(directory.Offset))
                {
                    throw new InvalidDataException("Cyclic Exif directory reference.");
                }
                if (completed.Contains(directory.Offset)) { continue; }
                active.Add(directory.Offset);
                pending.Push((directory.Offset, directory.Scope, true));
                if (directory.Offset < 8) { throw new InvalidDataException("Exif directory overlaps the TIFF header."); }
                int start = Position(directory.Offset, 2);
                int count = Short(start);
                Position(directory.Offset, checked(2 + count * 12 + 4));
                List<(uint Offset, string Scope)> children = [];
                List<MediaProperty> properties = [];
                for (int index = 0; index < count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    int entry = start + 2 + index * 12;
                    ushort tag = Short(entry);
                    ushort type = Short(entry + 2);
                    uint length = Long(entry + 4);
                    string name = ExifTagNames.Get(directory.Scope, tag);
                    MediaPropertyValue[] values = ReadValues(entry, type, length);
                    properties.Add(new("exif", name, values));
                    if (tag is 34665 or 34853 or 40965)
                    {
                        if (type != 4 || length != 1) { throw new InvalidDataException("Invalid Exif directory pointer."); }
                        string scope = tag switch
                        {
                            34665 => "Exif",
                            34853 => "GPS",
                            40965 => "Interoperability",
                            _ => throw new InvalidDataException("Invalid Exif directory tag.")
                        };
                        children.Add((Long(entry + 8), scope));
                    }
                }
                directories.Add(new(directory.Scope, properties));
                children.Add((Long(start + 2 + count * 12), directory.Scope + "/next"));
                for (int index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push((children[index].Offset, children[index].Scope, false));
                }
            }
            return new("Exif", children: directories);
        }

        private MediaPropertyValue[] ReadValues(int entry, int type, uint count)
        {
            int size = type switch
            {
                1 or 2 or 6 or 7 or 129 => 1,
                3 or 8 => 2,
                4 or 9 or 11 => 4,
                5 or 10 or 12 => 8,
                _ => throw new NotSupportedException($"Exif TIFF field type {type} is not supported.")
            };
            long length = (long)count * size;
            if (length > int.MaxValue) { throw new InvalidDataException("Exif value exceeds the supported byte range."); }
            int offset = entry + 8;
            if (length > 4)
            {
                uint position = Long(offset);
                if (position < 8) { throw new InvalidDataException("Exif value overlaps the TIFF header."); }
                offset = Position(position, (int)length);
            }
            ReadOnlySpan<byte> bytes = Bytes(offset, (int)length);
            if (type == 7 || count == 0) { return [MediaPropertyValue.FromBytes(bytes)]; }
            if (type is 2 or 129)
            {
                if (bytes[^1] != 0) { throw new InvalidDataException("Exif text is not null terminated."); }
                Encoding encoding = type switch
                {
                    2 => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                    129 => new UTF8Encoding(false, true),
                    _ => throw new InvalidDataException("Invalid Exif text type.")
                };
                return [encoding.GetString(bytes[..^1])];
            }
            MediaPropertyValue[] values = new MediaPropertyValue[count];
            for (int index = 0; index < values.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                int position = offset + index * size;
                values[index] = type switch
                {
                    1 => (uint)data.Span[position],
                    3 => (uint)Short(position),
                    4 => Long(position),
                    5 => Fraction(Long(position), Long(position + 4)),
                    6 => (int)unchecked((sbyte)data.Span[position]),
                    8 => (int)unchecked((short)Short(position)),
                    9 => unchecked((int)Long(position)),
                    10 => Fraction(unchecked((int)Long(position)), unchecked((int)Long(position + 4))),
                    11 => Real(BitConverter.Int32BitsToSingle(unchecked((int)Long(position)))),
                    12 => Real(BitConverter.Int64BitsToDouble(unchecked((long)Long64(position)))),
                    _ => throw new InvalidDataException("Invalid Exif numeric type.")
                };
            }
            return values;
        }

        private static MediaPropertyValue Real(double number)
        {
            if (!double.IsFinite(number)) { throw new InvalidDataException("Non-finite Exif number."); }
            return number;
        }

        private static MediaPropertyValue Fraction(long numerator, long denominator)
        {
            if (denominator == 0) { throw new InvalidDataException("Exif rational denominator is zero."); }
            return MediaPropertyValue.FromRational(numerator, denominator);
        }

        private int Position(uint offset, int length)
        {
            if (offset > data.Length || length > data.Length - offset)
            {
                throw new InvalidDataException("Exif directory or value exceeds its data extent.");
            }
            return (int)offset;
        }

        private ReadOnlySpan<byte> Bytes(int offset, int length) => data.Span.Slice(Position((uint)offset, length), length);
        private ushort Short(int offset) => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(Bytes(offset, 2)) : BinaryPrimitives.ReadUInt16BigEndian(Bytes(offset, 2));
        private uint Long(int offset) => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(Bytes(offset, 4)) : BinaryPrimitives.ReadUInt32BigEndian(Bytes(offset, 4));
        private ulong Long64(int offset) => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(Bytes(offset, 8)) : BinaryPrimitives.ReadUInt64BigEndian(Bytes(offset, 8));
    }
}
