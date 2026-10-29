// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Mediana.Tests
{
    internal static class PngTestInput
    {
        public static Dictionary<string, byte[]> ReadChunks(ReadOnlySpan<byte> png)
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (!png.Slice(0, 8).SequenceEqual(signature))
            {
                throw new InvalidDataException("Invalid PNG signature.");
            }
            Dictionary<string, byte[]> chunks = [];
            int offset = 8;
            while (offset < png.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.Slice(offset, 4));
                string type = Encoding.ASCII.GetString(png.Slice(offset + 4, 4));
                uint crc = uint.MaxValue;
                foreach (byte value in png.Slice(offset + 4, length + 4))
                {
                    crc ^= value;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        uint mask = 0u - (crc & 1u);
                        crc = (crc >> 1) ^ (0xedb88320u & mask);
                    }
                }
                if (~crc != BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset + 8 + length, 4)))
                {
                    throw new InvalidDataException($"PNG chunk {type} has an invalid CRC.");
                }
                chunks.Add(type, png.Slice(offset + 8, length).ToArray());
                offset = checked(offset + length + 12);
            }
            string[] order = ["IHDR", "sRGB", "pHYs", "IDAT", "IEND"];
            if (offset != png.Length || !chunks.Keys.SequenceEqual(order))
            {
                throw new InvalidDataException("Unexpected PNG chunk order or trailing data.");
            }
            return chunks;
        }

        public static byte[] Inflate(byte[] compressed)
        {
            using MemoryStream input = new(compressed, writable: false);
            using ZLibStream inflater = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            inflater.CopyTo(output);
            return output.ToArray();
        }

        public static void VerifyPreview(ReadOnlySpan<byte> png, int width, int height, ReadOnlySpan<byte> sourcePixels,
            int sourceWidth, Func<int, int, (int X, int Y)> sourcePosition, int tolerance)
        {
            Dictionary<string, byte[]> chunks = ReadChunks(png);
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
            header[8] = 8;
            header[9] = 6;
            byte[] physical = [0, 0, 0, 1, 0, 0, 0, 1, 0];
            if (!chunks["IHDR"].AsSpan().SequenceEqual(header) || !chunks["pHYs"].AsSpan().SequenceEqual(physical) ||
                !chunks["sRGB"].AsSpan().SequenceEqual(new byte[] { 0 }) || chunks["IEND"].Length != 0)
            {
                throw new InvalidDataException("The PNG preview dimensions, color or aspect differ from the reference.");
            }
            byte[] pixels = Inflate(chunks["IDAT"]);
            int stride = checked(width * 4 + 1);
            if (pixels.Length != checked(stride * height))
            {
                throw new InvalidDataException("Unexpected PNG scanline length.");
            }
            for (int y = 0; y < height; y++)
            {
                if (pixels[y * stride] != 0)
                {
                    throw new InvalidDataException("Unexpected PNG scanline filter.");
                }
                for (int x = 0; x < width; x++)
                {
                    (int sourceX, int sourceY) = sourcePosition(x, y);
                    int sourceOffset = checked((sourceY * sourceWidth + sourceX) * 4);
                    int outputOffset = y * stride + 1 + x * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        if (Math.Abs(pixels[outputOffset + channel] - sourcePixels[sourceOffset + channel]) > tolerance)
                        {
                            throw new InvalidDataException($"The PNG preview differs from its source reference at ({x}, {y}), channel {channel}.");
                        }
                    }
                }
            }
        }
    }
}
