// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class JpegTestData
    {
        internal static byte[] Segment(byte marker, ReadOnlySpan<byte> data)
        {
            byte[] result = new byte[data.Length + 4];
            result[0] = 255;
            result[1] = marker;
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), checked((ushort)(data.Length + 2)));
            data.CopyTo(result.AsSpan(4));
            return result;
        }

        internal static byte[] Image()
        {
            byte[] quantization = new byte[65];
            quantization.AsSpan(1).Fill(1);
            byte[] dc = new byte[18];
            dc[1] = 1;
            byte[] ac = dc.ToArray();
            ac[0] = 16;
            return [255, 216, .. Segment(219, quantization), .. Segment(192, [8, 0, 1, 0, 1, 1, 1, 17, 0]),
                .. Segment(196, [.. dc, .. ac]), .. Segment(218, [1, 1, 0, 0, 63, 0]), 63, 255, 217];
        }

        internal static int Find(byte[] encoded, byte marker)
        {
            for (int i = 2; i < encoded.Length - 1; i++)
            {
                if (encoded[i] == 255 && encoded[i + 1] == marker)
                {
                    return i;
                }
            }
            throw new InvalidDataException("The test JPEG marker is missing.");
        }

        internal static byte[] Insert(byte[] encoded, byte marker, ReadOnlySpan<byte> payload) =>
            [255, 216, .. Segment(marker, payload), .. encoded.AsSpan(2)];
    }
}
