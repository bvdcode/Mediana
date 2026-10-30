// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class AiffTestInput
    {
        public static byte[] Common(int bits = 16, int channels = 2, uint samples = 2, string? compression = null)
        {
            byte[] bytes = new byte[compression is null ? 18 : 24];
            BinaryPrimitives.WriteInt16BigEndian(bytes, (short)channels);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), samples);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(6), (short)bits);
            new byte[] { 0x40, 0x0e, 0xbb, 0x80, 0, 0, 0, 0, 0, 0 }.CopyTo(bytes, 8);
            if (compression is not null)
            {
                System.Text.Encoding.ASCII.GetBytes(compression).CopyTo(bytes, 18);
            }
            return bytes;
        }

        public static byte[] Configuration(int bits = 16, string? compression = null) =>
            [.. compression is null ? "AIFF"u8 : "AIFC"u8, .. Common(bits, compression: compression)];

        public static byte[] Sound(byte[] samples, int offset = 0, uint blockSize = 0, int trailingBytes = 0)
        {
            byte[] bytes = new byte[8 + offset + samples.Length + trailingBytes];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), blockSize);
            samples.CopyTo(bytes, 8 + offset);
            return bytes;
        }

        public static byte[] File(bool compressed, params (string Id, byte[] Bytes)[] chunks)
        {
            using MemoryStream stream = new();
            stream.Write("FORM"u8);
            stream.Write(new byte[4]);
            stream.Write(compressed ? "AIFC"u8 : "AIFF"u8);
            foreach ((string id, byte[] bytes) in chunks)
            {
                stream.Write(System.Text.Encoding.ASCII.GetBytes(id));
                byte[] size = new byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(size, (uint)bytes.Length);
                stream.Write(size);
                stream.Write(bytes);
                if ((bytes.Length & 1) != 0)
                {
                    stream.WriteByte(0);
                }
            }
            byte[] result = stream.ToArray();
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)(result.Length - 8));
            return result;
        }
    }
}
