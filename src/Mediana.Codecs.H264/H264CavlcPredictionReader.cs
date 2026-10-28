namespace Mediana.Codecs.H264
{
    internal readonly struct H264CavlcPredictionReader : IH264PredictionReader
    {
        public int ReadSubType(ref H264BitReader bits) => bits.ReadUe(3);

        public int ReadReference(ref H264BitReader bits, int x, int y, int maximum) =>
            maximum == 1 ? 1 - (int)bits.ReadBits(1) : bits.ReadUe(maximum);

        public (int X, int Y) ReadMotionDifference(ref H264BitReader bits, int x, int y, int width, int height) =>
            (bits.ReadSe(short.MinValue, short.MaxValue), bits.ReadSe(short.MinValue, short.MaxValue));
    }
}
