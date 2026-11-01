// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Containers.Matroska
{
    internal static class EbmlInteger
    {
        public static EbmlVint ReadVint(ReadOnlySpan<byte> source, int maximumWidth = 8)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumWidth, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumWidth, 8);
            if (source.IsEmpty)
            {
                throw new EndOfStreamException("The EBML variable integer is truncated.");
            }
            int width = BitOperations.LeadingZeroCount((uint)source[0]) - 23;
            if (width > maximumWidth)
            {
                throw new InvalidDataException("The EBML variable integer exceeds its permitted width.");
            }
            if (source.Length < width)
            {
                throw new EndOfStreamException("The EBML variable integer is truncated.");
            }
            ulong value = (uint)(source[0] & (0xFF >> width));
            for (int index = 1; index < width; index++)
            {
                value = (value << 8) | source[index];
            }
            return new(value, width);
        }

        public static uint ReadId(ReadOnlySpan<byte> source, out int width, int maximumWidth = 4)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumWidth, 4);
            EbmlVint value = ReadVint(source, maximumWidth);
            if (value.IsUnknown || (value.Width > 1 && value.Value < (1UL << (7 * (value.Width - 1))) - 1))
            {
                throw new InvalidDataException("The EBML element identifier is reserved or noncanonical.");
            }
            width = value.Width;
            return (uint)(value.Value | (1UL << (7 * width)));
        }

        public static long? ReadSize(ReadOnlySpan<byte> source, out int width, int maximumWidth = 8)
        {
            EbmlVint value = ReadVint(source, maximumWidth);
            width = value.Width;
            return value.IsUnknown ? null : (long)value.Value;
        }

        public static ulong ReadUnsigned(ReadOnlySpan<byte> data, ulong defaultValue)
        {
            if (data.Length > sizeof(ulong))
            {
                throw new InvalidDataException("An EBML unsigned integer cannot exceed eight bytes.");
            }
            if (data.IsEmpty)
            {
                return defaultValue;
            }
            ulong value = 0;
            foreach (byte octet in data)
            {
                value = (value << 8) | octet;
            }
            return value;
        }

        public static long ReadSigned(ReadOnlySpan<byte> data, long defaultValue)
        {
            ulong value = ReadUnsigned(data, unchecked((ulong)defaultValue));
            if (data.Length is > 0 and < sizeof(long) && (data[0] & 0x80) != 0)
            {
                value |= ulong.MaxValue << (data.Length * 8);
            }
            return unchecked((long)value);
        }
    }
}
