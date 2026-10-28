namespace Mediana.Codecs.H264
{
    internal interface IH264ResidualReader
    {
        int Read(ref H264BitReader bits, H264ResidualCategory category, int plane, int x, int y, scoped Span<int> coefficients);
    }
}
