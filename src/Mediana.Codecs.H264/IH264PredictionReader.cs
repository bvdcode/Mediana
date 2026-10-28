namespace Mediana.Codecs.H264
{
    internal interface IH264PredictionReader
    {
        int ReadSubType(ref H264BitReader bits);
        int ReadReference(ref H264BitReader bits, int x, int y, int maximum);
        (int X, int Y) ReadMotionDifference(ref H264BitReader bits, int x, int y, int width, int height);
    }
}
