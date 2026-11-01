// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace Mediana.Containers.Matroska
{
    internal static class EbmlValue
    {
        public static double ReadFloat(ReadOnlySpan<byte> data, double defaultValue) => data.Length switch
        {
            0 => defaultValue,
            sizeof(float) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data)),
            sizeof(double) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data)),
            _ => throw new InvalidDataException("An EBML float must contain zero, four or eight bytes.")
        };

        public static string ReadString(ReadOnlySpan<byte> data)
        {
            data = Terminated(data);
            foreach (byte value in data)
            {
                if (value is < 0x20 or > 0x7E)
                {
                    throw new InvalidDataException("An EBML string must contain printable ASCII characters.");
                }
            }
            return Encoding.ASCII.GetString(data);
        }

        public static string ReadUtf8(ReadOnlySpan<byte> data)
        {
            data = Terminated(data);
            if (!Utf8.IsValid(data))
            {
                throw new InvalidDataException("An EBML UTF-8 string contains an invalid encoding.");
            }
            return Encoding.UTF8.GetString(data);
        }

        private static ReadOnlySpan<byte> Terminated(ReadOnlySpan<byte> data)
        {
            int end = data.IndexOf((byte)0);
            return end < 0 ? data : data[..end];
        }
    }
}
