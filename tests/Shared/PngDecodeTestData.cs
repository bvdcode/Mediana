// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Mediana.Tests
{
    internal static class PngDecodeTestData
    {
        public static byte[] Header(int width = 1, int height = 1, byte depth = 8, byte color = 6)
        {
            byte[] result = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(result, width);
            BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), height);
            result[8] = depth;
            result[9] = color;
            return result;
        }

        public static byte[] Compressed(ReadOnlySpan<byte> filtered)
        {
            using MemoryStream output = new();
            using (ZLibStream zlib = new(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(filtered);
            }
            return output.ToArray();
        }

        public static byte[] Chunk(string type, ReadOnlySpan<byte> payload)
        {
            byte[] result = new byte[payload.Length + 12];
            BinaryPrimitives.WriteInt32BigEndian(result, payload.Length);
            Encoding.ASCII.GetBytes(type, result.AsSpan(4, 4));
            payload.CopyTo(result.AsSpan(8));
            uint crc = uint.MaxValue;
            foreach (byte value in result.AsSpan(4, payload.Length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
                }
            }
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(payload.Length + 8), ~crc);
            return result;
        }

        public static byte[] File(params byte[][] chunks) => [137, 80, 78, 71, 13, 10, 26, 10, .. chunks.SelectMany(chunk => chunk)];

        public static byte[] Image(ReadOnlySpan<byte> filtered, params byte[][] extra) => File(
            [Chunk("IHDR", Header()), .. extra, Chunk("IDAT", Compressed(filtered)), Chunk("IEND", [])]);
    }
}
