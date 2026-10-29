using System.Buffers.Binary;
using System.Text;

namespace Mediana.Tests
{
    internal static class FlacTestInput
    {
        public static byte[] StreamInfo(int minimum = 16, int maximum = 4096, int rate = 48000,
            int channels = 2, int bits = 16, long samples = 17760)
        {
            byte[] data = new byte[34];
            BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)minimum);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2), (ushort)maximum);
            ulong audio = (ulong)rate << 44 | (ulong)(channels - 1) << 41 | (ulong)(bits - 1) << 36 | (ulong)samples;
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(10), audio);
            return data;
        }

        public static byte[] File(byte[] audio, params (int Type, byte[] Data)[] blocks)
        {
            using MemoryStream output = new();
            output.Write("fLaC"u8);
            for (int index = 0; index < blocks.Length; index++)
            {
                (int type, byte[] data) = blocks[index];
                output.WriteByte((byte)(type | (index == blocks.Length - 1 ? 0x80 : 0)));
                output.WriteByte((byte)(data.Length >> 16));
                output.WriteByte((byte)(data.Length >> 8));
                output.WriteByte((byte)data.Length);
                output.Write(data);
            }
            output.Write(audio);
            return output.ToArray();
        }

        public static byte[] Comments(params string[] fields)
        {
            List<byte> bytes = [];
            WriteText(bytes, "Mediana test", littleEndian: true);
            WriteUInt32(bytes, (uint)fields.Length, littleEndian: true);
            foreach (string field in fields)
            {
                WriteText(bytes, field, littleEndian: true);
            }
            return bytes.ToArray();
        }

        public static byte[] Picture(uint type = 3, string mediaType = "image/png", byte[]? image = null)
        {
            List<byte> bytes = [];
            image ??= [1, 2, 3, 4];
            WriteUInt32(bytes, type);
            WriteText(bytes, mediaType);
            WriteText(bytes, "Обложка 🎵");
            WriteUInt32(bytes, 32);
            WriteUInt32(bytes, 32);
            WriteUInt32(bytes, 24);
            WriteUInt32(bytes, 0);
            WriteUInt32(bytes, (uint)image.Length);
            bytes.AddRange(image);
            return bytes.ToArray();
        }

        private static void WriteText(List<byte> output, string text, bool littleEndian = false)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            WriteUInt32(output, (uint)bytes.Length, littleEndian);
            output.AddRange(bytes);
        }

        private static void WriteUInt32(List<byte> output, uint value, bool littleEndian = false)
        {
            byte[] bytes = new byte[sizeof(uint)];
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            }
            output.AddRange(bytes);
        }
    }
}
