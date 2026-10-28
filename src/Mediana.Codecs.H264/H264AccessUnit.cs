namespace Mediana.Codecs.H264
{
    internal readonly record struct H264AccessUnit(H264Configuration Configuration, ReadOnlyMemory<byte> Slice,
        bool EndSequence, bool EndStream);
}
