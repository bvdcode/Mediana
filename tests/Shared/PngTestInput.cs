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
    }
}
