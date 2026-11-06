// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Metadata;

namespace Mediana.Formats.Tiff
{
    internal class TiffField(ushort tag, ushort type, ulong count, ReadOnlyMemory<byte> bytes, bool littleEndian)
    {
        internal ushort Tag { get; } = tag;
        internal ushort Type { get; } = type;
        internal ulong Count { get; } = count;
        internal ReadOnlyMemory<byte> Bytes { get; } = bytes;
        internal static int Size(int type) => type switch
        {
            1 or 2 or 6 or 7 or 129 => 1,
            3 or 8 => 2,
            4 or 9 or 11 or 13 => 4,
            5 or 10 or 12 or 16 or 17 or 18 => 8,
            _ => throw new NotSupportedException($"TIFF field type {type} is not supported.")
        };
        internal ulong Unsigned(int index = 0)
        {
            if ((ulong)index >= Count) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return Type switch
            {
                1 => Bytes.Span[index],
                3 => U16(index * 2),
                4 or 13 => U32(index * 4),
                16 or 18 => U64(index * 8),
                _ => throw new InvalidDataException("TIFF field requires an unsigned integer representation.")
            };
        }
        internal MediaPropertyValue[] Values(CancellationToken token)
        {
            ReadOnlySpan<byte> data = Bytes.Span;
            if (Type == 7 || Count == 0) { return [MediaPropertyValue.FromBytes(data)]; }
            if (Type is 2 or 129)
            {
                if (data[^1] != 0) { throw new InvalidDataException("Exif text is not null terminated."); }
                Encoding encoding = Type switch
                {
                    2 => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                    129 => new UTF8Encoding(false, true),
                    _ => throw new InvalidDataException("Invalid Exif text type.")
                };
                return [encoding.GetString(data[..^1])];
            }
            MediaPropertyValue[] values = new MediaPropertyValue[checked((int)Count)];
            int size = Size(Type);
            for (int index = 0; index < values.Length; index++)
            {
                token.ThrowIfCancellationRequested(); int position = index * size;
                values[index] = Type switch
                {
                    1 or 3 or 4 or 13 or 16 or 18 => Unsigned(index),
                    5 => Fraction(U32(position), U32(position + 4)),
                    6 => (int)unchecked((sbyte)data[position]),
                    8 => (int)unchecked((short)U16(position)),
                    9 => unchecked((int)U32(position)),
                    10 => Fraction(unchecked((int)U32(position)), unchecked((int)U32(position + 4))),
                    11 => Real(BitConverter.Int32BitsToSingle(unchecked((int)U32(position)))),
                    12 => Real(BitConverter.Int64BitsToDouble(unchecked((long)U64(position)))),
                    17 => unchecked((long)U64(position)),
                    _ => throw new InvalidDataException("Invalid Exif numeric type.")
                };
            }
            return values;
        }
        private static MediaPropertyValue Fraction(long numerator, long denominator)
        {
            if (denominator == 0 && numerator != 0) { throw new InvalidDataException("Exif rational denominator is zero."); }
            return MediaPropertyValue.FromRational(numerator, denominator);
        }
        private static MediaPropertyValue Real(double value)
        {
            if (!double.IsFinite(value)) { throw new InvalidDataException("Non-finite Exif number."); }
            return value;
        }
        private ushort U16(int position) => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(Bytes.Span[position..]) : BinaryPrimitives.ReadUInt16BigEndian(Bytes.Span[position..]);
        private uint U32(int position) => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(Bytes.Span[position..]) : BinaryPrimitives.ReadUInt32BigEndian(Bytes.Span[position..]);
        private ulong U64(int position) => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(Bytes.Span[position..]) : BinaryPrimitives.ReadUInt64BigEndian(Bytes.Span[position..]);
    }
}
