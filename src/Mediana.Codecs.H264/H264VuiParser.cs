namespace Mediana.Codecs.H264
{
    internal static class H264VuiParser
    {
        public static void Parse(ref H264BitReader bits)
        {
            if (bits.ReadFlag())
            {
                int aspectRatioIdc = (int)bits.ReadBits(8);
                if (aspectRatioIdc == 255)
                {
                    bits.ReadBits(16);
                    bits.ReadBits(16);
                }
            }
            if (bits.ReadFlag())
            {
                bits.ReadFlag();
            }
            if (bits.ReadFlag())
            {
                bits.ReadBits(3);
                bits.ReadFlag();
                if (bits.ReadFlag())
                {
                    bits.ReadBits(8);
                    bits.ReadBits(8);
                    bits.ReadBits(8);
                }
            }
            if (bits.ReadFlag())
            {
                bits.ReadUe(5);
                bits.ReadUe(5);
            }
            if (bits.ReadFlag())
            {
                if (bits.ReadBits(32) == 0 || bits.ReadBits(32) == 0)
                {
                    throw new InvalidDataException("H.264 VUI timing values must be nonzero.");
                }
                bits.ReadFlag();
            }
            bool nalHrd = bits.ReadFlag();
            if (nalHrd)
            {
                ReadHrd(ref bits);
            }
            bool vclHrd = bits.ReadFlag();
            if (vclHrd)
            {
                ReadHrd(ref bits);
            }
            if (nalHrd || vclHrd)
            {
                bits.ReadFlag();
            }
            bits.ReadFlag();
            if (bits.ReadFlag())
            {
                bits.ReadFlag();
                bits.ReadUe(16);
                bits.ReadUe(16);
                bits.ReadUe(16);
                bits.ReadUe(16);
                bits.ReadUe();
                bits.ReadUe();
            }
        }

        private static void ReadHrd(ref H264BitReader bits)
        {
            int cpbCount = bits.ReadUe(31) + 1;
            bits.ReadBits(4);
            bits.ReadBits(4);
            for (int index = 0; index < cpbCount; index++)
            {
                bits.ReadUe();
                bits.ReadUe();
                bits.ReadFlag();
            }
            bits.ReadBits(5);
            bits.ReadBits(5);
            bits.ReadBits(5);
            bits.ReadBits(5);
        }
    }
}
