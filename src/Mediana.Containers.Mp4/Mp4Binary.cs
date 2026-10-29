// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4Binary
    {
        public static ReadOnlySpan<byte> Range(ReadOnlySpan<byte> data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
            {
                throw new InvalidDataException("An MP4 field extends beyond its box.");
            }
            return data.Slice(offset, length);
        }

        public static ushort UInt16(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadUInt16BigEndian(Range(data, offset, 2));

        public static uint UInt32(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadUInt32BigEndian(Range(data, offset, 4));

        public static int Int32(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadInt32BigEndian(Range(data, offset, 4));

        public static ulong UInt64(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadUInt64BigEndian(Range(data, offset, 8));

        public static long Int64(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadInt64BigEndian(Range(data, offset, 8));

        public static string FourCc(ReadOnlySpan<byte> data, int offset) =>
            Encoding.Latin1.GetString(Range(data, offset, 4));

        public static void FullBox(ReadOnlySpan<byte> data, byte version = 0, bool allowFlags = false)
        {
            uint header = UInt32(data, 0);
            if ((header >> 24) != version || (!allowFlags && (header & 0xFFFFFF) != 0))
            {
                throw new NotSupportedException("The MP4 box version or flags are not supported.");
            }
        }

        public static long Offset64(ulong value)
        {
            if (value > long.MaxValue)
            {
                throw new NotSupportedException("MP4 values above the signed 64-bit range are not supported.");
            }
            return (long)value;
        }
    }
}
