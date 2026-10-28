using System.Buffers.Binary;

namespace Mediana.Codecs.Png
{
    internal static class PngChunk
    {
        private static readonly uint[] CrcTable = CreateCrcTable();

        public static int Write(Span<byte> destination, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload,
            CancellationToken cancellationToken)
        {
            BinaryPrimitives.WriteInt32BigEndian(destination, payload.Length);
            type.CopyTo(destination.Slice(4, 4));
            payload.CopyTo(destination.Slice(8, payload.Length));
            Finish(destination, payload.Length, cancellationToken);
            return checked(payload.Length + 12);
        }

        public static void Finish(Span<byte> chunk, int payloadLength, CancellationToken cancellationToken)
        {
            BinaryPrimitives.WriteInt32BigEndian(chunk, payloadLength);
            ReadOnlySpan<byte> data = chunk.Slice(4, checked(payloadLength + 4));
            uint crc = uint.MaxValue;
            for (int index = 0; index < data.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                crc = CrcTable[(crc ^ data[index]) & 255] ^ (crc >> 8);
            }
            BinaryPrimitives.WriteUInt32BigEndian(chunk.Slice(8 + payloadLength, 4), ~crc);
        }

        private static uint[] CreateCrcTable()
        {
            uint[] result = new uint[256];
            for (uint index = 0; index < result.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 0 ? value >> 1 : (value >> 1) ^ 0xedb88320u;
                }
                result[index] = value;
            }
            return result;
        }
    }
}
