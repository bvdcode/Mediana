using System.Buffers.Binary;
using System.Text;
using Mediana.Core.Audio;

namespace Mediana.Tests
{
    internal static class Mp3TestInput
    {
        public static byte[] Frame(string header = "FFFB9064", byte fill = 0)
        {
            byte[] bytes = Convert.FromHexString(header);
            byte[] frame = new byte[Mp3FrameHeader.Read(bytes).FrameBytes];
            frame.AsSpan().Fill(fill);
            bytes.CopyTo(frame, 0);
            return frame;
        }

        public static byte[] Xing(uint frames, string header = "FFFB9064")
        {
            byte[] frame = Frame(header);
            int offset = 4 + Mp3FrameHeader.Read(frame.AsSpan(0, 4)).SideInfoBytes;
            "Xing"u8.CopyTo(frame.AsSpan(offset));
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(offset + 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(offset + 8), frames);
            return frame;
        }

        public static byte[] Tag(int version, byte[] body, int flags = 0)
        {
            byte[] header = [(byte)'I', (byte)'D', (byte)'3', (byte)version, 0, (byte)flags, .. Synchsafe(body.Length)];
            if (version == 4 && (flags & 0x10) != 0)
            {
                return [.. header, .. body, (byte)'3', (byte)'D', (byte)'I', .. header.AsSpan(3).ToArray()];
            }
            return [.. header, .. body];
        }

        public static byte[] MetadataFrame(string id, byte[] body, int version = 4, int flags = 0)
        {
            byte[] size = new byte[4];
            if (version == 4)
            {
                size = Synchsafe(body.Length);
            }
            else
            {
                BinaryPrimitives.WriteInt32BigEndian(size, body.Length);
            }
            return [.. Encoding.ASCII.GetBytes(id), .. size, (byte)(flags >> 8), (byte)flags, .. body];
        }

        public static byte[] Picture(byte[] bytes, string description = "cover", int encoding = 3)
        {
            byte[] text = encoding switch
            {
                0 => Encoding.Latin1.GetBytes(description),
                1 => [0xff, 0xfe, .. Encoding.Unicode.GetBytes(description)],
                2 => Encoding.BigEndianUnicode.GetBytes(description),
                3 => Encoding.UTF8.GetBytes(description),
                _ => throw new ArgumentOutOfRangeException(nameof(encoding))
            };
            byte[] terminator = encoding is 1 or 2 ? [0, 0] : [0];
            return [(byte)encoding, .. "image/png"u8.ToArray(), 0, 3, .. text, .. terminator, .. bytes];
        }

        public static byte[] Synchsafe(int value) =>
            [(byte)(value >> 21 & 127), (byte)(value >> 14 & 127), (byte)(value >> 7 & 127), (byte)(value & 127)];

        public static byte[] Unsynchronize(byte[] input)
        {
            List<byte> bytes = [];
            foreach (byte value in input)
            {
                bytes.Add(value);
                if (value == 0xff)
                {
                    bytes.Add(0);
                }
            }
            return bytes.ToArray();
        }
    }
}
