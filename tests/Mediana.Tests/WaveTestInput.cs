using System.Buffers.Binary;
using System.Text;

namespace Mediana.Tests
{
    internal static class WaveTestInput
    {
        public static byte[] Format(int bits = 16, int channels = 2, int rate = 48000, int tag = 1,
            int? validBits = null, uint mask = 3, bool floating = false)
        {
            byte[] result = new byte[tag switch { 0xfffe => 40, 1 => 16, _ => 18 }];
            BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)tag);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)rate);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), (uint)(rate * channels * bits / 8));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), (ushort)(channels * bits / 8));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), (ushort)bits);
            if (tag == 0xfffe)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(16), 22);
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(18), (ushort)(validBits ?? bits));
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(20), mask);
                Guid subtype = floating ? new("00000003-0000-0010-8000-00aa00389b71") : new("00000001-0000-0010-8000-00aa00389b71");
                subtype.TryWriteBytes(result.AsSpan(24));
            }
            return result;
        }

        public static byte[] File(params (string Id, byte[] Data)[] chunks)
        {
            byte[] result = new byte[12 + chunks.Sum(chunk => 8 + chunk.Data.Length + (chunk.Data.Length & 1))];
            "RIFF"u8.CopyTo(result);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length - 8);
            "WAVE"u8.CopyTo(result.AsSpan(8));
            int offset = 12;
            foreach ((string id, byte[] data) in chunks)
            {
                Encoding.ASCII.GetBytes(id, result.AsSpan(offset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset + 4), (uint)data.Length);
                data.CopyTo(result.AsSpan(offset + 8));
                offset += 8 + data.Length + (data.Length & 1);
            }
            return result;
        }
    }
}
