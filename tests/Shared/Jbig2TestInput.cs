// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class Jbig2TestInput
    {
        public static byte[] TwoPageFile(byte[] first, byte[] second)
        {
            List<byte[]> firstParts = Parts(first);
            List<byte[]> secondParts = Parts(second);
            return [.. first.AsSpan(0, 8), 3,
                .. Segment(1, 48, 1, firstParts[0]), .. Segment(2, 38, 1, firstParts[1]), .. Segment(3, 49, 1, []),
                .. Segment(4, 48, 300, secondParts[0]), .. Segment(5, 38, 300, secondParts[1]), .. Segment(6, 49, 300, []),
                .. Segment(7, 51, 0, [])];
        }

        public static List<byte[]> Parts(byte[] input)
        {
            int offset = (input[8] & 2) != 0 ? 9 : 13;
            List<byte[]> result = [];
            while (offset < input.Length)
            {
                int flags = input[offset + 4];
                int references = input[offset + 5] >> 5;
                int header = 6 + references + ((flags & 64) == 0 ? 1 : 4) + 4;
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(offset + header - 4, 4)));
                result.Add(input.AsSpan(offset + header, length).ToArray());
                offset += header + length;
            }
            return result;
        }

        public static byte[] Segment(int number, int type, uint page, byte[] payload, byte? reference = null)
        {
            int references = reference.HasValue ? 1 : 0;
            byte[] result = new byte[14 + references + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)number);
            result[4] = (byte)(type | 64);
            result[5] = (byte)(references << 5);
            if (reference.HasValue)
            {
                result[6] = reference.Value;
            }
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(6 + references), page);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(10 + references), (uint)payload.Length);
            payload.CopyTo(result, 14 + references);
            return result;
        }
    }
}
