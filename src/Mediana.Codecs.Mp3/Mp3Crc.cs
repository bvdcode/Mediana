namespace Mediana.Codecs.Mp3
{
    internal static class Mp3Crc
    {
        public static ushort Update(ushort state, ReadOnlySpan<byte> data)
        {
            uint value = state;
            foreach (byte item in data)
            {
                value ^= (uint)item << 8;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = ((value << 1) ^ ((value & 0x8000) != 0 ? 0x8005u : 0)) & 0xffff;
                }
            }
            return (ushort)value;
        }
    }
}
