namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4SampleLocation(long Offset, int Size, long DecodeTime,
        long PresentationTime, uint Duration, bool IsSync);
}
