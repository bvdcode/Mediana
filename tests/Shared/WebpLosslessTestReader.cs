// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class WebpLosslessTestReader
    {
        internal static byte[] Decode(ReadOnlyMemory<byte> file, out int width, out int height)
        {
            if (!file.Span[..4].SequenceEqual("RIFF"u8) || !file.Span.Slice(8, 8).SequenceEqual("WEBPVP8L"u8)
                || BinaryPrimitives.ReadUInt32LittleEndian(file.Span[4..]) != file.Length - 8 || file.Span[20] != 47)
            {
                throw new InvalidDataException("Invalid reference WebP RIFF file.");
            }
            WebpTestBitReader reader = new(file[21..]);
            width = reader.Read(14) + 1;
            height = reader.Read(14) + 1;
            reader.Read(1);
            if (reader.Read(3) != 0) { throw new InvalidDataException("Invalid VP8L version."); }
            int codedWidth = width;
            List<(int Type, int Width, int Bits, uint[] Data)> transforms = [];
            while (reader.Read(1) != 0)
            {
                int type = reader.Read(2);
                switch (type)
                {
                    case 0:
                        int bits = reader.Read(3) + 2;
                        uint[] modes = ReadImage(reader, (codedWidth + (1 << bits) - 1) >> bits, (height + (1 << bits) - 1) >> bits, false);
                        transforms.Add((type, codedWidth, bits, modes));
                        break;
                    case 2:
                        transforms.Add((type, codedWidth, 0, []));
                        break;
                    case 3:
                        int count = reader.Read(8) + 1;
                        uint[] palette = ReadImage(reader, count, 1, false);
                        for (int index = 1; index < count; index++) { palette[index] = Add(palette[index], palette[index - 1]); }
                        int packed = count switch { <= 2 => 3, <= 4 => 2, <= 16 => 1, _ => 0 };
                        transforms.Add((type, codedWidth, packed, palette));
                        codedWidth = (codedWidth + (1 << packed) - 1) >> packed;
                        break;
                    default: throw new NotSupportedException("Unexpected transform in WebP encoder reference output.");
                }
            }
            uint[] pixels = ReadImage(reader, codedWidth, height, true);
            for (int transform = transforms.Count - 1; transform >= 0; transform--)
            {
                (int type, int originalWidth, int bits, uint[] data) = transforms[transform];
                switch (type)
                {
                    case 0:
                        int columns = (originalWidth + (1 << bits) - 1) >> bits;
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < originalWidth; x++)
                            {
                                int index = y * originalWidth + x;
                                uint predicted;
                                if (y == 0) { predicted = x == 0 ? 0xff000000 : pixels[index - 1]; }
                                else if (x == 0) { predicted = pixels[index - originalWidth]; }
                                else
                                {
                                    int mode = (int)((data[(y >> bits) * columns + (x >> bits)] >> 8) & 255);
                                    predicted = mode switch
                                    {
                                        0 => 0xff000000,
                                        1 => pixels[index - 1],
                                        2 => pixels[index - originalWidth],
                                        12 => Gradient(pixels[index - 1], pixels[index - originalWidth], pixels[index - originalWidth - 1]),
                                        _ => throw new InvalidDataException("Unexpected WebP encoder predictor.")
                                    };
                                }
                                pixels[index] = Add(pixels[index], predicted);
                            }
                        }
                        break;
                    case 2:
                        for (int index = 0; index < pixels.Length; index++)
                        {
                            uint green = (pixels[index] >> 8) & 255;
                            pixels[index] = Add(pixels[index], green | (green << 16));
                        }
                        break;
                    case 3:
                        uint[] unpacked = new uint[originalWidth * height];
                        int indexBits = 8 >> bits;
                        int mask = (1 << indexBits) - 1;
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < originalWidth; x++)
                            {
                                int packedWidth = (originalWidth + (1 << bits) - 1) >> bits;
                                int value = (int)((pixels[y * packedWidth + (x >> bits)] >> 8) & 255);
                                int paletteIndex = (value >> ((x & ((1 << bits) - 1)) * indexBits)) & mask;
                                unpacked[y * originalWidth + x] = data[paletteIndex];
                            }
                        }
                        pixels = unpacked;
                        break;
                    default: throw new InvalidDataException("Unexpected WebP inverse transform.");
                }
            }
            byte[] result = new byte[width * height * 4];
            for (int index = 0; index < pixels.Length; index++) { BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * 4), pixels[index]); }
            return result;
        }

        private static uint[] ReadImage(WebpTestBitReader reader, int width, int height, bool main)
        {
            if (reader.Read(1) != 0 || (main && reader.Read(1) != 0))
            {
                throw new NotSupportedException("Unexpected color cache or spatial entropy groups in encoder reference output.");
            }
            WebpTestHuffman green = WebpTestHuffman.Read(reader, 280);
            WebpTestHuffman red = WebpTestHuffman.Read(reader, 256);
            WebpTestHuffman blue = WebpTestHuffman.Read(reader, 256);
            WebpTestHuffman alpha = WebpTestHuffman.Read(reader, 256);
            WebpTestHuffman distance = WebpTestHuffman.Read(reader, 40);
            uint[] pixels = new uint[width * height];
            for (int index = 0; index < pixels.Length;)
            {
                int symbol = green.Decode(reader);
                if (symbol < 256)
                {
                    uint r = (uint)red.Decode(reader);
                    uint b = (uint)blue.Decode(reader);
                    uint a = (uint)alpha.Decode(reader);
                    pixels[index++] = r | ((uint)symbol << 8) | (b << 16) | (a << 24);
                }
                else
                {
                    int length = Length(reader, symbol - 256);
                    int code = Length(reader, distance.Decode(reader));
                    int offset = code switch
                    {
                        1 => width,
                        2 => 1,
                        > 120 => code - 120,
                        _ => throw new NotSupportedException("Unexpected WebP encoder distance mapping.")
                    };
                    for (int copy = 0; copy < length; copy++) { pixels[index] = pixels[index - offset]; index++; }
                }
            }
            return pixels;
        }

        private static int Length(WebpTestBitReader reader, int symbol)
        {
            if (symbol < 4) { return symbol + 1; }
            int bits = (symbol - 2) / 2;
            return ((2 + (symbol & 1)) << bits) + reader.Read(bits) + 1;
        }

        private static uint Add(uint first, uint second)
        {
            uint value = 0;
            for (int shift = 0; shift < 32; shift += 8) { value |= (((first >> shift) + (second >> shift)) & 255) << shift; }
            return value;
        }

        private static uint Gradient(uint left, uint top, uint corner)
        {
            uint value = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int channel = (int)((left >> shift) & 255) + (int)((top >> shift) & 255) - (int)((corner >> shift) & 255);
                value |= (uint)Math.Clamp(channel, 0, 255) << shift;
            }
            return value;
        }
    }
}
