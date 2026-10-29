namespace Mediana.Tests
{
    internal static class AacTestPackets
    {
        public static byte[] Mono(bool noise = false)
        {
            AacTestBitWriter writer = new AacTestBitWriter().Write(0, 3).Write(0, 4).Write(100, 8);
            Window(writer);
            if (noise)
            {
                writer.Write(13, 4).Write(1, 5).Write(256, 9).Write(0, 3);
            }
            else
            {
                Spectrum(writer);
            }
            return writer.Write(7, 3).ToArray();
        }

        public static byte[] Stereo(bool intensity = false, int maskMode = 0)
        {
            AacTestBitWriter writer = new AacTestBitWriter().Write(1, 3).Write(0, 4).Write(1, 1);
            Window(writer);
            writer.Write(maskMode, 2);
            if (maskMode == 1)
            {
                writer.Write(1, 1);
            }
            writer.Write(100, 8);
            Spectrum(writer);
            writer.Write(100, 8);
            if (intensity)
            {
                writer.Write(15, 4).Write(1, 5).Write(0, 1).Write(0, 3);
            }
            else
            {
                Spectrum(writer);
            }
            return writer.Write(7, 3).ToArray();
        }

        private static void Window(AacTestBitWriter writer) => writer.Write(0, 4).Write(1, 6).Write(0, 1);

        private static void Spectrum(AacTestBitWriter writer) =>
            writer.Write(1, 4).Write(1, 5).Write(0, 1).Write(0, 3).Write(2036, 11);
    }
}
