namespace Mediana.Codecs.H264
{
    internal interface IH264PredictionReader
    {
        int ReadSubType(ref H264BitReader bits, H264SliceKind kind);
        int ReadReference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height, int maximum);
        (int X, int Y) ReadMotionDifference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height);
    }
}
