namespace Mediana.Codecs.H264
{
    internal record H264PredictionWeight(int LumaWeight, int LumaOffset, int CbWeight, int CbOffset, int CrWeight, int CrOffset);
}
