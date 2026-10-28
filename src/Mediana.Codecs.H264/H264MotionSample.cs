namespace Mediana.Codecs.H264
{
    internal readonly record struct H264MotionSample(bool Available, int ReferenceIndex, int X, int Y);
}
